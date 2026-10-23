package com.colin.clipbridge;
import android.app.*;
import android.content.*;
import android.os.Build;
import android.Manifest;
import android.content.pm.PackageManager;

final class Notices {
    static void received(Context c,int resource){
        // Android 13+ supplies clipboard-write feedback; avoid a duplicate result.
        if(Build.VERSION.SDK_INT>=33){Config.status(c,resource);c.getSystemService(NotificationManager.class).cancel(1);}
        else show(c,resource);
    }
    static Notification create(Context c,String text){
        return builder(c,text).build();
    }
    static Notification.Builder builder(Context c,String text){
        NotificationManager manager=c.getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel("sync",c.getString(R.string.notification_channel),NotificationManager.IMPORTANCE_LOW));
        PendingIntent open=PendingIntent.getActivity(c,0,new Intent(c,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(c,"sync").setSmallIcon(android.R.drawable.ic_menu_share).setContentTitle(c.getString(R.string.app_name)).setContentText(text).setContentIntent(open).setAutoCancel(true).setVisibility(Notification.VISIBILITY_PRIVATE);
    }
    static void show(Context c,int resource,Object... args){
        String text=c.getString(resource,args);Config.status(c,resource,args);
        if(Build.VERSION.SDK_INT<33||c.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)==PackageManager.PERMISSION_GRANTED)c.getSystemService(NotificationManager.class).notify(1,create(c,text));
    }
}
