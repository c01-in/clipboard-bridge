package com.colin.clipbridge;

import android.app.*;
import android.content.*;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Build;
import android.view.*;
import android.widget.*;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.work.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

/** Runs real Android content grants/foreground service/Downloads against the actual C# file endpoint. */
@RunWith(AndroidJUnit4.class)
public final class FileIntegrationTests {
    Instrumentation instrumentation;Context context;JSONObject config;Activity activity;
    @Before public void setup()throws Exception{
        Assume.assumeTrue("Start windows/Tests --file-integration-host, then pass -e fileIntegration true", "true".equals(InstrumentationRegistry.getArguments().getString("fileIntegration")));
        instrumentation=InstrumentationRegistry.getInstrumentation();context=instrumentation.getTargetContext();
        Assume.assumeTrue(context.getPackageName().endsWith(".debug"));
        config=new JSONObject().put("endpoints",new JSONArray().put("http://10.0.2.2:45879"));
        JSONObject remote=new JSONObject(Network.request(config,"/test/config",null,4096));
        config=remote;
        Config.prefs(context).edit().clear().commit();Config.save(context,config);
        Config.prefs(context).edit().putString("receiveGeneration",remote.getString("generation")).putString("registeredGeneration",remote.getString("generation")).commit();
        if(Build.VERSION.SDK_INT>=33)instrumentation.getUiAutomation().grantRuntimePermission(context.getPackageName(),android.Manifest.permission.POST_NOTIFICATIONS);
    }
    @After public void cleanup()throws Exception{
        if(context==null)return;
        if(activity!=null)instrumentation.runOnMainSync(()->activity.finish());
        WorkManager.getInstance(context).cancelAllWork().getResult().get(5,TimeUnit.SECONDS);
        File[] journals=new File(context.getFilesDir(),"file-receipts").listFiles();
        if(journals!=null)for(File journal:journals){
            if(journal.getName().endsWith(".json")){
                JSONArray files=new JSONObject(new String(java.nio.file.Files.readAllBytes(journal.toPath()),StandardCharsets.UTF_8)).getJSONArray("files");
                for(int i=0;i<files.length();i++)context.getContentResolver().delete(Uri.parse(files.getJSONObject(i).getString("uri")),null,null);
            }
            journal.delete();
        }
        context.getSystemService(NotificationManager.class).cancelAll();Config.prefs(context).edit().clear().commit();
    }
    @Test public void externalMultiFileShareUsesForegroundServiceAndActualDesktopSaving()throws Exception{
        JSONArray before=new JSONArray(Network.request(config,"/test/saved",null,65536));
        Instrumentation.ActivityMonitor monitor=instrumentation.addMonitor(FileShareActivity.class.getName(),null,false);
        context.startActivity(new Intent().setClassName(instrumentation.getContext().getPackageName(),ShareSourceActivity.class.getName()).putExtra("mode","files").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
        activity=monitor.waitForActivityWithTimeout(10000);instrumentation.removeMonitor(monitor);assertNotNull("Multi-share did not reach the file screen",activity);
        long until=System.currentTimeMillis()+120000;boolean progressSeen=false,saved=false;
        while(System.currentTimeMillis()<until){
            FileSendService.State state=FileSendService.state;
            if(state!=null&&state.bytes>0&&state.running){progressSeen=true;captureActivityView("android-file-progress-view.png",true);}
            try{Network.request(config,"/test/save","{}",4096);saved=true;}catch(Exception notReady){}
            if(state!=null&&!state.running){assertTrue(state.error,state.success);break;}
            Thread.sleep(100);
        }
        assertNotNull(FileSendService.state);assertFalse("Transfer did not stop",FileSendService.state.running);assertTrue(FileSendService.state.error,FileSendService.state.success);assertTrue(saved);assertTrue("No real transfer progress was observed",progressSeen);
        JSONArray batches=new JSONArray(Network.request(config,"/test/saved",null,65536));assertEquals(before.length()+1,batches.length());
        JSONArray files=batches.getJSONArray(batches.length()-1);assertEquals(3,files.length());
        String[] originals={"large.bin","fixture.png","empty.dat"};
        for(int i=0;i<originals.length;i++){
            String name=originals[i];
            Uri source=Uri.parse("content://"+instrumentation.getContext().getPackageName()+".fixture/"+name);
            try(InputStream input=context.getContentResolver().openInputStream(source)){assertEquals(hash(input),files.getJSONObject(i).getString("sha256"));}
        }
        assertTrue(files.getJSONObject(0).getLong("size")>16*1024*1024);
        assertEquals("File sharing must not use clipboard sequence tracking",0,Config.prefs(context).getLong("outgoingSequence",0));
        captureActivityView("android-files-sent-view.png",false);
    }
    @Test public void windowsMultiFileOfferSavesToRealAndroidDownloadsAndConfirms()throws Exception{
        JSONObject offer=new JSONObject(Network.request(config,"/test/offer","{}",65536));
        OneTimeWorkRequest request=new OneTimeWorkRequest.Builder(FileReceiveWorker.class).setInputData(new Data.Builder().putString("box",offer.getString("box")).build()).setExpedited(OutOfQuotaPolicy.RUN_AS_NON_EXPEDITED_WORK_REQUEST).build();
        WorkManager.getInstance(context).enqueue(request).getResult().get(5,TimeUnit.SECONDS);
        WorkInfo info=null;long until=System.currentTimeMillis()+120000;
        while(System.currentTimeMillis()<until){info=WorkManager.getInstance(context).getWorkInfoById(request.getId()).get(5,TimeUnit.SECONDS);if(info!=null&&info.getState().isFinished())break;Thread.sleep(100);}
        assertNotNull(info);assertEquals(WorkInfo.State.SUCCEEDED,info.getState());
        File journal=new File(new File(context.getFilesDir(),"file-receipts"),offer.getString("id")+".json");
        JSONArray receipts=new JSONObject(new String(java.nio.file.Files.readAllBytes(journal.toPath()),StandardCharsets.UTF_8)).getJSONArray("files");
        JSONArray expected=offer.getJSONArray("files");assertEquals(2,receipts.length());
        for(int i=0;i<receipts.length();i++){
            assertTrue(receipts.getJSONObject(i).getBoolean("done"));Uri uri=Uri.parse(receipts.getJSONObject(i).getString("uri"));assertEquals("media",uri.getAuthority());
            try(InputStream input=context.getContentResolver().openInputStream(uri)){assertEquals(expected.getJSONObject(i).getString("sha256"),hash(input));}
        }
        assertEquals("saved",new JSONObject(Network.request(config,"/test/status",null,4096)).getString("status"));
        assertEquals("Receiving files must not apply a clipboard update",0,Config.prefs(context).getLong("lastSequence",0));
        // A repeated worker reuses saved URIs, so a lost final receipt cannot duplicate Downloads.
        OneTimeWorkRequest retry=new OneTimeWorkRequest.Builder(FileReceiveWorker.class).setInputData(new Data.Builder().putString("box",offer.getString("box")).build()).build();
        WorkManager.getInstance(context).enqueue(retry).getResult().get(5,TimeUnit.SECONDS);
        until=System.currentTimeMillis()+10000;
        while(System.currentTimeMillis()<until){info=WorkManager.getInstance(context).getWorkInfoById(retry.getId()).get(5,TimeUnit.SECONDS);if(info!=null&&info.getState().isFinished())break;Thread.sleep(100);}
        assertNotNull(info);assertEquals(WorkInfo.State.SUCCEEDED,info.getState());
        assertEquals(receipts.toString(),new JSONObject(new String(java.nio.file.Files.readAllBytes(journal.toPath()),StandardCharsets.UTF_8)).getJSONArray("files").toString());
    }
    @Test public void cancelAfterAChunkDoesNotSaveOrApplyClipboard()throws Exception{
        int before=new JSONArray(Network.request(config,"/test/saved",null,65536)).length();
        File directory=new File(context.getFilesDir(),"clips");directory.mkdirs();String name=FileTransfer.id()+".png";File file=new File(directory,name);
        Bitmap image=Bitmap.createBitmap(8,8,Bitmap.Config.ARGB_8888);image.eraseColor(0xff7044ff);
        try(FileOutputStream output=new FileOutputStream(file)){image.compress(Bitmap.CompressFormat.PNG,100,output);}finally{image.recycle();}
        java.util.concurrent.atomic.AtomicBoolean cancelled=new java.util.concurrent.atomic.AtomicBoolean();
        try{
            Uri uri=Uri.parse("content://"+context.getPackageName()+".images/"+name);
            try{FileTransfer.send(context,Collections.singletonList(uri),cancelled::get,(index,count,bytes,total,label,saving)->{if(bytes>0)cancelled.set(true);});fail("Cancelled transfer reported success");}
            catch(InterruptedException expected){assertTrue(cancelled.get());}
            try{Network.request(config,"/test/save","{}",4096);fail("Cancelled batch was ready for saving");}catch(Exception expected){assertNotNull(expected.getMessage());}
            assertEquals(before,new JSONArray(Network.request(config,"/test/saved",null,65536)).length());
            assertEquals(0,Config.prefs(context).getLong("outgoingSequence",0));
        }finally{file.delete();}
    }
    private static String hash(InputStream input)throws Exception{MessageDigest digest=MessageDigest.getInstance("SHA-256");byte[] buffer=new byte[65536];int n;while((n=input.read(buffer))!=-1)digest.update(buffer,0,n);return FileTransfer.hex(digest.digest());}
    private void captureActivityView(String name,boolean requireProgress)throws Exception{
        File output=new File(context.getFilesDir(),name);if(output.exists())return;
        // AOSP ATD disables display rendering. Capture the actual laid-out Activity view,
        // not a mocked layout or a fabricated full-screen screenshot.
        Bitmap[] image={null};
        instrumentation.runOnMainSync(()->{
            View decor=activity.getWindow().getDecorView();
            if(decor.getWidth()<=0||decor.getHeight()<=0||requireProgress&&!hasProgress(decor))return;
            image[0]=Bitmap.createBitmap(decor.getWidth(),decor.getHeight(),Bitmap.Config.ARGB_8888);decor.draw(new android.graphics.Canvas(image[0]));
        });
        if(image[0]==null)return;
        try(FileOutputStream stream=new FileOutputStream(output)){image[0].compress(Bitmap.CompressFormat.PNG,100,stream);}finally{image[0].recycle();}
    }
    private boolean hasProgress(View view){if(view instanceof ProgressBar&&((ProgressBar)view).getProgress()>0)return true;if(view instanceof ViewGroup)for(int i=0;i<((ViewGroup)view).getChildCount();i++)if(hasProgress(((ViewGroup)view).getChildAt(i)))return true;return false;}
}
