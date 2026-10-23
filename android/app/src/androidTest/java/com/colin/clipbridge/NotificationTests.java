package com.colin.clipbridge;

import android.app.*;
import android.content.*;
import android.os.Build;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.util.Arrays;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

/** Exercise real posted notifications and the launcher lifecycle, not badge preferences. */
@RunWith(AndroidJUnit4.class)
public final class NotificationTests {
    Instrumentation instrumentation;Context context;NotificationManager manager;Activity activity;
    @Before public void setup(){
        instrumentation=InstrumentationRegistry.getInstrumentation();context=instrumentation.getTargetContext();
        assertTrue("Never alter the release app",context.getPackageName().endsWith(".debug"));
        manager=context.getSystemService(NotificationManager.class);manager.cancelAll();
        Config.prefs(context).edit().clear().putBoolean("notificationIntroShown",true).commit();
        if(Build.VERSION.SDK_INT>=33)instrumentation.getUiAutomation().grantRuntimePermission(context.getPackageName(),android.Manifest.permission.POST_NOTIFICATIONS);
    }
    @After public void cleanup(){
        if(activity!=null)instrumentation.runOnMainSync(activity::finish);
        manager.cancelAll();Config.prefs(context).edit().clear().commit();
    }
    void open(){activity=instrumentation.startActivitySync(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER).setClass(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));}
    boolean has(int id){return Arrays.stream(manager.getActiveNotifications()).anyMatch(n->n.getId()==id);}
    @Test public void launcherOpenPreservesResultsAndOngoingTransfer()throws Exception{
        manager.notify(1,Notices.create(context,"test status"));
        manager.notify("sent",21,Notices.create(context,"test sent"));
        manager.notify(23,Notices.create(context,"test received"));
        Notification progress=Notices.create(context,"test transfer");progress.flags|=Notification.FLAG_ONGOING_EVENT;
        manager.notify(20,progress);Thread.sleep(150);
        assertTrue(has(1));assertTrue(has(21));assertTrue(has(23));assertTrue(has(20));
        open();Thread.sleep(1800);
        assertTrue(has(1));assertTrue(has(21));assertTrue(has(23));
        assertTrue("Opening the app removed an ongoing transfer notice",has(20));
    }
    @Test public void resultsStayVisibleUntilTheUserDismissesThem()throws Exception{
        open();manager.notify(21,Notices.create(context,"test result while visible"));Thread.sleep(1800);assertTrue(has(21));
        instrumentation.runOnMainSync(activity::finish);activity=null;instrumentation.waitForIdleSync();
        manager.notify(23,Notices.create(context,"test unread background result"));Thread.sleep(1800);
        assertTrue("Unread background result disappeared before opening",has(23));
        open();Thread.sleep(1800);assertTrue(has(21));assertTrue(has(23));
    }
    @Test public void openingAppKeepsOngoingSyncAndFileProgress()throws Exception{
        Notification progress=Notices.create(context,"test active transfer");progress.flags|=Notification.FLAG_ONGOING_EVENT;
        for(int id:new int[]{1,20,22})manager.notify(id,progress);
        Thread.sleep(150);open();Thread.sleep(1800);
        for(int id:new int[]{1,20,22})assertTrue("Active progress notification was removed: "+id,has(id));
    }
}
