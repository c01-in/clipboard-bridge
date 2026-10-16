package com.colin.clipbridge;

import android.app.Activity;
import android.app.Instrumentation;
import android.app.NotificationManager;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Build;
import android.widget.TextView;
import android.view.View;
import android.view.ViewGroup;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.io.*;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Base64;
import java.util.concurrent.*;
import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

/** Device tests use a separate debug app and an authenticated local protocol peer. */
@RunWith(AndroidJUnit4.class)
public class ShareTests {
    private Instrumentation instrumentation;
    private Context context;
    private Activity activity;
    @Before public void before(){
        instrumentation=InstrumentationRegistry.getInstrumentation();context=instrumentation.getTargetContext();
        // Never reset real pairing or configuration when someone runs the wrong variant.
        Assume.assumeTrue(context.getPackageName().endsWith(".debug"));
        Config.prefs(context).edit().clear().commit();
    }
    @After public void after(){
        if(activity!=null)instrumentation.runOnMainSync(()->activity.finish());
        if(context!=null&&context.getPackageName().endsWith(".debug")){
            context.getSystemService(NotificationManager.class).cancelAll();Config.prefs(context).edit().clear().commit();
            File[] files=new File(context.getFilesDir(),"clips").listFiles();if(files!=null)for(File file:files)file.delete();
        }
    }
    private Intent text(String value){return new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT,value);}
    private void rejects(Intent intent)throws Exception{try{ShareInput.capture(intent);fail("Invalid share accepted");}catch(Exception expected){assertNotNull(expected.getMessage());}}
    @Test public void textAndClipDataFallback()throws Exception{
        String sample="Share → Windows\n中文 📋";assertEquals(sample,ShareInput.capture(text(sample)).text);
        Intent fallback=new Intent(Intent.ACTION_SEND).setType("text/plain");fallback.setClipData(ClipData.newPlainText("test",sample));
        assertEquals(sample,ShareInput.capture(fallback).text);
    }
    @Test public void imageUriAndClipDataFallback()throws Exception{
        Uri uri=Uri.parse("content://example.test/one.png");
        assertEquals(uri,ShareInput.capture(new Intent(Intent.ACTION_SEND).setType("image/png").putExtra(Intent.EXTRA_STREAM,uri)).image);
        Intent fallback=new Intent(Intent.ACTION_SEND).setType("image/png");fallback.setClipData(ClipData.newRawUri("test",uri));
        assertEquals(uri,ShareInput.capture(fallback).image);
    }
    @Test public void rejectsUnsupportedAndMultipleContent()throws Exception{
        rejects(null);rejects(new Intent(Intent.ACTION_SEND_MULTIPLE).setType("image/png"));
        rejects(new Intent(Intent.ACTION_VIEW).setType("text/plain"));rejects(text(""));
        rejects(new Intent(Intent.ACTION_SEND).setType("text/html").putExtra(Intent.EXTRA_TEXT,"<b>test</b>"));
        rejects(new Intent(Intent.ACTION_SEND).setType("image/png").putExtra(Intent.EXTRA_STREAM,Uri.parse("file:///etc/hosts")));
        rejects(text("test").putExtra(Intent.EXTRA_STREAM,Uri.parse("content://example.test/private")));
        ClipData many=ClipData.newPlainText("test","first");many.addItem(new ClipData.Item("second"));
        Intent multiple=text("first");multiple.setClipData(many);rejects(multiple);
        rejects(new Intent(Intent.ACTION_SEND).setType("image/png").putExtra(Intent.EXTRA_STREAM,"not a URI"));
    }
    @Test public void shareSheetResolvesBothTypes(){
        for(String type:new String[]{"text/plain","image/jpeg"}){
            Intent intent=new Intent(Intent.ACTION_SEND).setType(type).setPackage(context.getPackageName());
            assertTrue(context.getPackageManager().queryIntentActivities(intent,0).stream().anyMatch(info->info.activityInfo.name.equals(ShareActivity.class.getName())));
        }
        assertTrue(context.getPackageManager().queryIntentActivities(new Intent(Intent.ACTION_SEND).setType("application/pdf").setPackage(context.getPackageName()),0).isEmpty());
    }
    @Test public void successfulReceiveHasNoDuplicateNotificationOnModernAndroid()throws Exception{
        if(Build.VERSION.SDK_INT>=33)instrumentation.getUiAutomation().grantRuntimePermission(context.getPackageName(),android.Manifest.permission.POST_NOTIFICATIONS);
        assertTrue("Notifications must be enabled for this regression test",context.getSystemService(NotificationManager.class).areNotificationsEnabled());
        Notices.show(context,R.string.receiving);
        awaitNotificationCount(1);
        Notices.received(context,R.string.received_text);
        assertEquals("received_text",Config.prefs(context).getString("statusKey",""));
        if(Build.VERSION.SDK_INT>=33)awaitNotificationCount(0);
    }
    private void awaitNotificationCount(int expected)throws Exception{
        // NotificationManager enqueues/cancels asynchronously in system_server.
        long until=System.currentTimeMillis()+3000;
        while(System.currentTimeMillis()<until){if(context.getSystemService(NotificationManager.class).getActiveNotifications().length==expected)return;Thread.sleep(25);}
        assertEquals("Unexpected active notification count",expected,context.getSystemService(NotificationManager.class).getActiveNotifications().length);
    }
    @Test public void shareActivitySendsUnicodeOnceAcrossRecreation()throws Exception{
        String sample="Shared directly → Windows\n中文 📋";
        try(Peer peer=new Peer()){
            configure(peer);activity=launch(text(sample));
            peer.request.get(10,TimeUnit.SECONDS);
            instrumentation.runOnMainSync(()->activity.recreate());instrumentation.waitForIdleSync();
            peer.allowReceipt.countDown();
            assertArrayEquals(sample.getBytes(StandardCharsets.UTF_8),peer.completed.get(10,TimeUnit.SECONDS));
            assertEquals("text/plain",peer.mime);assertEquals(1,peer.requests);
        }
    }
    @Test public void shareActivitySendsActualPngBytes()throws Exception{
        try(Peer peer=new Peer()){
            configure(peer);
            Bitmap bitmap=Bitmap.createBitmap(8,8,Bitmap.Config.ARGB_8888);bitmap.eraseColor(0x807044ff);
            byte[] png;try(ByteArrayOutputStream out=new ByteArrayOutputStream()){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);png=out.toByteArray();}finally{bitmap.recycle();}
            String name="0123456789abcdef0123456789abcdef.png";
            File folder=new File(context.getFilesDir(),"clips");folder.mkdirs();
            try(FileOutputStream out=new FileOutputStream(new File(folder,name))){out.write(png);}
            Uri uri=Uri.parse("content://"+context.getPackageName()+".images/"+name);
            activity=launch(new Intent(Intent.ACTION_SEND).setType("image/png").putExtra(Intent.EXTRA_STREAM,uri).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));
            peer.allowReceipt.countDown();
            assertArrayEquals(png,peer.completed.get(10,TimeUnit.SECONDS));assertEquals("image/png",peer.mime);
        }
    }
    @Test public void pausedShareDoesNotContactComputer()throws Exception{
        try(Peer peer=new Peer()){
            configure(peer);Config.prefs(context).edit().putBoolean("paused",true).commit();activity=launch(text("must not send"));
            awaitMessage(context.getString(R.string.send_failed,context.getString(R.string.sync_paused)));
            assertEquals(0,peer.requests);
        }
    }
    @Test public void unpairedShareOpensActionableError()throws Exception{
        activity=launch(text("must not send"));awaitMessage(context.getString(R.string.send_failed,context.getString(R.string.status_unpaired)));
    }
    @Test public void badReceiptDoesNotReportSuccess()throws Exception{
        try(Peer peer=new Peer()){
            configure(peer);peer.wrongReceipt=true;activity=launch(text("receipt test"));peer.allowReceipt.countDown();
            peer.completed.get(10,TimeUnit.SECONDS);awaitMessage(context.getString(R.string.send_failed,context.getString(R.string.error_receipt)));
        }
    }
    @Test public void externalAppSharesTextAndGrantedImage()throws Exception{
        for(String mode:new String[]{"text","image"}){
            try(Peer peer=new Peer()){
                configure(peer);peer.allowReceipt.countDown();
                Instrumentation.ActivityMonitor monitor=instrumentation.addMonitor(ShareActivity.class.getName(),null,false);
                context.startActivity(new Intent().setClassName(instrumentation.getContext().getPackageName(),ShareSourceActivity.class.getName()).putExtra("mode",mode).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
                activity=monitor.waitForActivityWithTimeout(10000);instrumentation.removeMonitor(monitor);assertNotNull("Share target was not launched",activity);
                byte[] content=peer.completed.get(10,TimeUnit.SECONDS);
                if("text".equals(mode))assertEquals("External source → Windows 📋",new String(content,StandardCharsets.UTF_8));
                else{
                    android.graphics.Bitmap image=android.graphics.BitmapFactory.decodeByteArray(content,0,content.length);
                    assertNotNull(image);assertEquals(8,image.getWidth());assertEquals(8,image.getHeight());assertEquals(0x807044ff,image.getPixel(0,0));image.recycle();
                }
                assertEquals("text".equals(mode)?"text/plain":"image/png",peer.mime);
                instrumentation.runOnMainSync(()->activity.finish());activity=null;
            }
        }
    }
    @Test public void ordinaryCopyInAnotherAppDoesNotSendOrNotify()throws Exception{
        try(Peer peer=new Peer()){
            configure(peer);long before=Config.prefs(context).getLong("outgoingSequence",0);
            context.startActivity(new Intent().setClassName(instrumentation.getContext().getPackageName(),ShareSourceActivity.class.getName()).putExtra("mode","copy").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
            instrumentation.waitForIdleSync();Thread.sleep(2000);
            assertEquals(before,Config.prefs(context).getLong("outgoingSequence",0));assertEquals(0,peer.requests);
            assertEquals(0,context.getSystemService(NotificationManager.class).getActiveNotifications().length);
        }
    }
    private Activity launch(Intent intent){
        return instrumentation.startActivitySync(intent.setClass(context,ShareActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
    }
    private void configure(Peer peer)throws Exception{
        Config.save(context,new JSONObject().put("key",peer.key).put("endpoints",new JSONArray().put("http://127.0.0.1:"+peer.server.getLocalPort())));
        Config.prefs(context).edit().putString("registeredGeneration",SyncState.generation(context)).commit();
    }
    private void awaitMessage(String expected)throws Exception{
        long until=System.currentTimeMillis()+10000;
        while(System.currentTimeMillis()<until){
            boolean[] found={false};instrumentation.runOnMainSync(()->found[0]=contains(activity.getWindow().getDecorView(),expected));
            if(found[0])return;Thread.sleep(50);
        }
        fail("Expected share result was not displayed: "+expected);
    }
    private boolean contains(View view,String text){
        if(view instanceof TextView&&text.contentEquals(((TextView)view).getText()))return true;
        if(view instanceof ViewGroup)for(int i=0;i<((ViewGroup)view).getChildCount();i++)if(contains(((ViewGroup)view).getChildAt(i),text))return true;
        return false;
    }
    private static final class Peer implements AutoCloseable {
        final ServerSocket server=new ServerSocket(0,4,java.net.InetAddress.getByName("127.0.0.1"));
        final ExecutorService executor=Executors.newSingleThreadExecutor();
        final CompletableFuture<Void> request=new CompletableFuture<>();
        final CompletableFuture<byte[]> completed=new CompletableFuture<>();
        final CountDownLatch allowReceipt=new CountDownLatch(1);
        final String key=Base64.getEncoder().encodeToString(new byte[32]); // An isolated, public test key.
        volatile int requests;volatile String mime;volatile boolean wrongReceipt;
        Peer()throws Exception{
            executor.execute(()->{
                try(Socket socket=server.accept()){
                    socket.setSoTimeout(10000);InputStream input=socket.getInputStream();
                    String first=line(input);if(!first.startsWith("POST /api/clipboard "))throw new IOException("Unexpected request path");
                    int length=0;String header;while(!(header=line(input)).isEmpty())if(header.toLowerCase(java.util.Locale.ROOT).startsWith("content-length:"))length=Integer.parseInt(header.substring(15).trim());
                    if(length<=0||length>24*1024*1024)throw new IOException("Invalid request size");
                    byte[] payload=new byte[length];new DataInputStream(input).readFully(payload);
                    JSONObject body=new JSONObject(new String(payload,StandardCharsets.UTF_8));
                    JSONObject manifest=new JSONObject(new String(Crypto.open(body.getString("envelope"),key),StandardCharsets.UTF_8));
                    byte[] bytes=Crypto.open(body.getString("data"),key);mime=manifest.getString("mime");
                    assertEquals(bytes.length,manifest.getInt("size"));
                    StringBuilder hash=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(bytes))hash.append(String.format("%02x",b&255));
                    assertEquals(hash.toString(),manifest.getString("sha256"));requests++;request.complete(null);
                    if(!allowReceipt.await(10,TimeUnit.SECONDS))throw new IOException("Receipt gate timed out");
                    JSONObject receipt=new JSONObject().put("id",wrongReceipt?"wrong":manifest.getString("id")).put("sequence",manifest.getLong("sequence")).put("status","applied");
                    byte[] response=new JSONObject().put("data",Crypto.seal(receipt.toString().getBytes(StandardCharsets.UTF_8),key)).toString().getBytes(StandardCharsets.UTF_8);
                    OutputStream out=socket.getOutputStream();out.write(("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "+response.length+"\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));out.write(response);out.flush();
                    // A second connection would reveal an accidental resend after recreation.
                    server.setSoTimeout(750);
                    try(Socket duplicate=server.accept()){requests++;}catch(java.net.SocketTimeoutException expected){}
                    completed.complete(bytes);
                }catch(Throwable error){request.completeExceptionally(error);completed.completeExceptionally(error);}
            });
        }
        private static String line(InputStream input)throws IOException{
            ByteArrayOutputStream bytes=new ByteArrayOutputStream();int b;while((b=input.read())!=-1){if(b=='\n')return bytes.toString(StandardCharsets.US_ASCII.name()).replace("\r","");if(bytes.size()>=8192)throw new IOException("Header too long");bytes.write(b);}throw new EOFException();
        }
        public void close()throws Exception{allowReceipt.countDown();server.close();executor.shutdownNow();}
    }
}
