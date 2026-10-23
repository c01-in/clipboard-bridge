package com.colin.clipbridge;

import android.app.*;
import android.content.*;
import android.net.Uri;
import android.os.Build;
import android.provider.DocumentsContract;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.atomic.AtomicReference;
import org.json.*;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public final class ReceivedFilesTests {
    private Context context;private Instrumentation instrumentation;private NotificationManager manager;
    private File journal,image;private String id;private Uri uri;private JSONObject record;private Activity host;
    @Before public void setup()throws Exception{
        instrumentation=InstrumentationRegistry.getInstrumentation();context=instrumentation.getTargetContext();
        assertTrue("Never alter the release app",context.getPackageName().endsWith(".debug"));
        manager=context.getSystemService(NotificationManager.class);
        if(Build.VERSION.SDK_INT>=33)instrumentation.getUiAutomation().grantRuntimePermission(context.getPackageName(),android.Manifest.permission.POST_NOTIFICATIONS);
        id=FileTransfer.id();File directory=new File(context.getFilesDir(),"file-receipts");assertTrue(directory.isDirectory()||directory.mkdirs());journal=new File(directory,id+".json");
        File clips=new File(context.getFilesDir(),"clips");assertTrue(clips.isDirectory()||clips.mkdirs());image=new File(clips,id+".png");
        try(FileOutputStream stream=new FileOutputStream(image)){stream.write(java.util.Base64.getDecoder().decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII="));}
        uri=Uri.parse("content://"+context.getPackageName()+".images/"+id+".png");
        record=new JSONObject().put("complete",true).put("acknowledged",true).put("folder","")
            .put("files",new JSONArray().put(new JSONObject().put("uri",uri.toString()).put("done",true)));
        FileReceiveWorker.persist(journal,record);
        Config.prefs(context).edit().putBoolean("notificationIntroShown",true).commit();
        host=instrumentation.startActivitySync(new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
    }
    @After public void cleanup(){if(host!=null)instrumentation.runOnMainSync(host::finish);manager.cancelAll();if(journal!=null)journal.delete();if(image!=null)image.delete();}
    private Intent tap(PendingIntent action,String expectedAction)throws Exception{
        AtomicReference<Intent> launched=new AtomicReference<>();
        Instrumentation.ActivityMonitor monitor=new Instrumentation.ActivityMonitor(){
            @Override public Instrumentation.ActivityResult onStartActivity(Intent intent){
                if(expectedAction.equals(intent.getAction())){launched.set(new Intent(intent));return new Instrumentation.ActivityResult(Activity.RESULT_CANCELED,null);}
                return null;
            }
        };
        instrumentation.addMonitor(monitor);
        try{action.send();long until=android.os.SystemClock.elapsedRealtime()+5000;while(launched.get()==null&&android.os.SystemClock.elapsedRealtime()<until)Thread.sleep(25);assertNotNull("Notification action did not launch its system intent",launched.get());return launched.get();}
        finally{instrumentation.removeMonitor(monitor);}
    }
    @Test public void singleFileNotificationOpensExactlyTheSavedUriWithReadOnlyGrant()throws Exception{
        Notification notification=ReceivedFilesActivity.notification(context,"Saved 1 file",id,record);manager.notify(id,23,notification);
        assertEquals(2,notification.actions.length);assertEquals(context.getString(R.string.files_open_file),notification.actions[0].title.toString());
        assertEquals(context.getString(R.string.files_view_location),notification.actions[1].title.toString());
        Intent open=tap(notification.actions[0].actionIntent,Intent.ACTION_VIEW);
        assertEquals(uri,open.getData());assertEquals("image/png",open.getType());assertEquals(uri,open.getClipData().getItemAt(0).getUri());
        assertEquals(Intent.FLAG_GRANT_READ_URI_PERMISSION,open.getFlags()&(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION));
    }
    @Test public void openingOneReceiptPreservesOtherFileAndClipboardNotifications()throws Exception{
        String other=FileTransfer.id();
        Notification notification=ReceivedFilesActivity.notification(context,"Saved 1 file",id,record);
        manager.notify(id,23,notification);manager.notify(other,23,Notices.create(context,"another saved batch"));
        Notices.received(context,R.string.received_image,other);
        Thread.sleep(100);
        tap(notification.actions[0].actionIntent,Intent.ACTION_VIEW);
        Thread.sleep(100);
        boolean original=false,another=false,clipboard=false;
        for(android.service.notification.StatusBarNotification item:manager.getActiveNotifications()){
            original|=id.equals(item.getTag())&&item.getId()==23;
            another|=other.equals(item.getTag())&&item.getId()==23;
            clipboard|=other.equals(item.getTag())&&item.getId()==24;
        }
        assertFalse(original);assertTrue("Other file notification disappeared",another);assertTrue("Clipboard notification disappeared",clipboard);
    }
    @Test public void multipleFileNotificationShowsTheSavedLocationEvenAfterPreferenceChanges()throws Exception{
        String tree="content://com.android.externalstorage.documents/tree/primary%3ADocuments";
        record.put("folder",tree);record.getJSONArray("files").put(new JSONObject().put("uri",uri.toString()).put("done",true));FileReceiveWorker.persist(journal,record);
        Notification notification=ReceivedFilesActivity.notification(context,"Saved 2 files",id,record);assertEquals(1,notification.actions.length);
        Intent browse=tap(notification.contentIntent,Intent.ACTION_OPEN_DOCUMENT);
        assertEquals(DocumentsContract.buildDocumentUriUsingTree(Uri.parse(tree),"primary:Documents"),browse.getParcelableExtra(DocumentsContract.EXTRA_INITIAL_URI));
        assertTrue(browse.hasCategory(Intent.CATEGORY_OPENABLE));assertEquals("*/*",browse.getType());assertNull(browse.getData());
    }
    @Test public void defaultLocationUsesDownloadsWithoutRequestingDirectoryAccess()throws Exception{
        Intent browse=ReceivedFilesActivity.locationIntent(record);
        assertEquals(DocumentsContract.buildDocumentUri("com.android.externalstorage.documents","primary:Download/Clipboard Bridge"),browse.getParcelableExtra(DocumentsContract.EXTRA_INITIAL_URI));
        assertEquals(Intent.ACTION_OPEN_DOCUMENT,browse.getAction());assertNull(browse.getData());
        assertEquals(0,browse.getFlags()&(Intent.FLAG_GRANT_WRITE_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION));
    }
    @Test public void incompleteMissingAndUntrustedReceiptPathsAreRejected()throws Exception{
        for(String bad:new String[]{null,"../outside",FileTransfer.id()})try{ReceivedFilesActivity.readReceipt(context,bad);fail("Invalid receipt accepted");}catch(Exception expected){assertNotNull(expected.getMessage());}
        record.put("acknowledged",false);FileReceiveWorker.persist(journal,record);
        try{ReceivedFilesActivity.readReceipt(context,id);fail("Incomplete transfer accepted");}catch(Exception expected){assertNotNull(expected.getMessage());}
        record.put("acknowledged",true);record.getJSONArray("files").getJSONObject(0).put("uri","file:///private/file");FileReceiveWorker.persist(journal,record);
        try{ReceivedFilesActivity.readReceipt(context,id);fail("Raw file URI accepted");}catch(Exception expected){assertNotNull(expected.getMessage());}
        assertFalse(context.getPackageManager().getActivityInfo(new ComponentName(context,ReceivedFilesActivity.class),0).exported);
    }
}
