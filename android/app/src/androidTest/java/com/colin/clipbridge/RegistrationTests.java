package com.colin.clipbridge;

import android.app.*;
import android.content.*;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.work.*;
import java.io.*;
import java.lang.reflect.Field;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

/** Isolated debug-app regression tests. Loopback acknowledgments are not Google delivery tests. */
@RunWith(AndroidJUnit4.class)
public final class RegistrationTests {
    Context context;Instrumentation instrumentation;Activity activity;
    static final String VAPID=String.join("",Collections.nCopies(87,"A"));
    static final String KEY=Base64.getEncoder().encodeToString(new byte[32]);
    @Before public void setup()throws Exception{
        instrumentation=InstrumentationRegistry.getInstrumentation();context=instrumentation.getTargetContext();
        assertTrue("Never alter the release app",context.getPackageName().endsWith(".debug"));
        WorkManager.getInstance(context).cancelAllWork().getResult().get(5,TimeUnit.SECONDS);
        Config.prefs(context).edit().clear().putBoolean("notificationIntroShown",true).commit();resetWaiting(null,"");
    }
    @After public void cleanup()throws Exception{
        if(activity!=null)instrumentation.runOnMainSync(()->activity.finish());
        WorkManager.getInstance(context).cancelAllWork().getResult().get(5,TimeUnit.SECONDS);resetWaiting(null,"");
    }
    JSONObject subscription()throws Exception{return new JSONObject().put("endpoint","https://fcm.googleapis.com/wp/isolated-test").put("keys",new JSONObject().put("p256dh","test").put("auth","test"));}
    JSONObject configure(int port,boolean registered)throws Exception{
        JSONObject config=new JSONObject().put("key",KEY).put("vapid",VAPID).put("endpoints",new JSONArray().put("http://127.0.0.1:"+port));
        Config.save(context,config);Config.saveSubscription(context,new JSONObject().put("vapid",VAPID).put("subscription",subscription()));
        Config.prefs(context).edit().putString("requestedVapid",VAPID).commit();
        if(registered)SyncState.registered(context,SyncState.device(context,subscription()));
        Config.status(context,R.string.status_paired);return config;
    }
    @Test public void completedSessionDoesNotDisplayOldRestoringStatus()throws Exception{
        configure(1,true);Config.status(context,R.string.sync_restoring);
        assertTrue(SyncState.ready(context));assertEquals(context.getString(R.string.status_paired),Config.statusText(context));
    }
    @Test public void unconfirmedSessionDoesNotPretendToBeReady()throws Exception{
        configure(1,false);assertFalse(SyncState.ready(context));
        assertEquals(context.getString(R.string.sync_restoring),Config.statusText(context));
    }
    @Test public void refreshFailureDoesNotRevokeAcknowledgedSession()throws Exception{
        configure(1,true);Config.status(context,R.string.registration_failed,"refresh timeout");
        assertEquals(context.getString(R.string.status_paired),Config.statusText(context));
        PushRegistration.refresh(context);
        assertEquals(subscription().toString(),Config.subscription(context).getJSONObject("subscription").toString());
        awaitRegistrationFinished();
        assertTrue(SyncState.ready(context));
        assertEquals(context.getString(R.string.status_paired),Config.statusText(context));
    }
    @Test public void unconfirmedRegistrationFailureRemainsVisible()throws Exception{
        configure(1,false);Config.status(context,R.string.registration_failed,"test failure");
        assertEquals(context.getString(R.string.registration_failed,"test failure"),Config.statusText(context));
        assertFalse(SyncState.ready(context));
    }
    @Test public void openingAcknowledgedSessionDoesNotStartRegistration()throws Exception{
        configure(1,true);
        activity=instrumentation.startActivitySync(new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        Thread.sleep(400);
        assertEquals(subscription().toString(),Config.subscription(context).getJSONObject("subscription").toString());
        assertTrue(WorkManager.getInstance(context).getWorkInfosForUniqueWork("register-device").get(5,TimeUnit.SECONDS).stream().noneMatch(w->!w.getState().isFinished()));
        assertTrue(SyncState.ready(context));
    }
    @Test public void changedPushEndpointRequiresComputerAcknowledgment()throws Exception{
        configure(1,true);
        JSONObject rotated=subscription().put("endpoint","https://fcm.googleapis.com/wp/isolated-rotated");
        PushRegistration.completed(context,rotated);
        assertFalse(SyncState.ready(context));
        assertEquals(rotated.toString(),Config.subscription(context).getJSONObject("subscription").toString());
    }
    @Test public void openingAppPreservesSubscriptionAndEndpointCallbackDoesNotCancelHandshake()throws Exception{
        try(Peer peer=new Peer()){
            configure(peer.server.getLocalPort(),true);
            RegistrationWorker.retry(context);
            activity=instrumentation.startActivitySync(new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            peer.received.get(8,TimeUnit.SECONDS);
            WorkInfo before=WorkManager.getInstance(context).getWorkInfosForUniqueWork("register-device").get(5,TimeUnit.SECONDS).stream().filter(w->w.getState()==WorkInfo.State.RUNNING).findFirst().orElseThrow();
            assertEquals(subscription().toString(),Config.subscription(context).getJSONObject("subscription").toString());
            assertEquals(context.getString(R.string.status_paired),Config.statusText(context));
            PushRegistration.completed(context,subscription());Thread.sleep(350);
            assertNotEquals(WorkInfo.State.CANCELLED,WorkManager.getInstance(context).getWorkInfoById(before.getId()).get(5,TimeUnit.SECONDS).getState());
            peer.allowAck.countDown();peer.completed.get(8,TimeUnit.SECONDS);
            long deadline=System.currentTimeMillis()+8000;WorkInfo.State state;
            do{state=WorkManager.getInstance(context).getWorkInfoById(before.getId()).get(5,TimeUnit.SECONDS).getState();if(state.isFinished())break;Thread.sleep(50);}while(System.currentTimeMillis()<deadline);
            assertEquals(WorkInfo.State.SUCCEEDED,state);assertTrue(SyncState.ready(context));
            assertEquals(context.getString(R.string.status_paired),Config.statusText(context));
        }
    }
    void awaitRegistrationFinished()throws Exception{
        long deadline=System.currentTimeMillis()+10000;
        do{
            List<WorkInfo> work=WorkManager.getInstance(context).getWorkInfosForUniqueWork("register-device").get(5,TimeUnit.SECONDS);
            if(!work.isEmpty()&&work.stream().allMatch(w->w.getState().isFinished())){
                assertEquals(WorkInfo.State.SUCCEEDED,work.get(work.size()-1).getState());return;
            }
            Thread.sleep(50);
        }while(System.currentTimeMillis()<deadline);
        fail("Registration refresh did not finish");
    }
    @Test public void registrationTimeoutReleasesPendingRequestForRetry()throws Exception{
        JSONObject config=configure(1,false);Config.prefs(context).edit().remove("subscription").remove("subscriptionIv").commit();
        resetWaiting(new CompletableFuture<JSONObject>(),VAPID);
        try{PushRegistration.ensure(context,config);fail("Unanswered registration did not time out");}
        catch(IOException expected){assertEquals(context.getString(R.string.registration_timeout),expected.getMessage());}
        Field field=PushRegistration.class.getDeclaredField("waiting");field.setAccessible(true);assertNull(field.get(null));
    }
    static void resetWaiting(CompletableFuture<JSONObject> future,String vapid)throws Exception{
        Field waiting=PushRegistration.class.getDeclaredField("waiting");waiting.setAccessible(true);waiting.set(null,future);
        Field requested=PushRegistration.class.getDeclaredField("requested");requested.setAccessible(true);requested.set(null,vapid);
    }
    static final class Peer implements AutoCloseable{
        final ServerSocket server=new ServerSocket(0,1,InetAddress.getByName("127.0.0.1"));
        final ExecutorService executor=Executors.newSingleThreadExecutor();
        final CompletableFuture<Void> received=new CompletableFuture<>(),completed=new CompletableFuture<>();
        final CountDownLatch allowAck=new CountDownLatch(1);
        Peer()throws IOException{executor.execute(()->{
            try(Socket client=server.accept()){
                client.setSoTimeout(8000);InputStream in=client.getInputStream();
                if(!line(in).startsWith("POST /api/register "))throw new IOException("Unexpected registration path");
                int length=0;String header;while(!(header=line(in)).isEmpty())if(header.toLowerCase(Locale.ROOT).startsWith("content-length:"))length=Integer.parseInt(header.substring(15).trim());
                if(length<=0||length>16384)throw new IOException("Invalid registration size");
                byte[] body=new byte[length];new DataInputStream(in).readFully(body);
                JSONObject box=new JSONObject(new String(body,StandardCharsets.UTF_8));
                JSONObject device=new JSONObject(new String(Crypto.open(box.getString("data"),KEY),StandardCharsets.UTF_8));
                received.complete(null);if(!allowAck.await(6,TimeUnit.SECONDS))throw new IOException("Test acknowledgment gate timed out");
                JSONObject ack=new JSONObject().put("registered",true).put("challenge",device.getString("challenge")).put("receiveGeneration",device.getString("receiveGeneration")).put("revision",device.getLong("revision")).put("paused",device.getBoolean("paused"));
                byte[] response=new JSONObject().put("data",Crypto.seal(ack.toString().getBytes(StandardCharsets.UTF_8),KEY)).toString().getBytes(StandardCharsets.UTF_8);
                OutputStream out=client.getOutputStream();out.write(("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "+response.length+"\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));out.write(response);out.flush();completed.complete(null);
            }catch(Throwable e){received.completeExceptionally(e);completed.completeExceptionally(e);}
        });}
        static String line(InputStream in)throws IOException{ByteArrayOutputStream out=new ByteArrayOutputStream();int b;while((b=in.read())!=-1){if(b=='\n')return out.toString(StandardCharsets.US_ASCII.name()).replace("\r","");if(out.size()>8192)throw new IOException("Header too large");out.write(b);}throw new EOFException();}
        public void close()throws IOException{allowAck.countDown();server.close();executor.shutdownNow();}
    }
}
