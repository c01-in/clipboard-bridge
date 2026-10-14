package com.colin.clipbridge;
import android.app.*;
import android.content.*;
import android.os.Build;
import android.Manifest;
import android.content.pm.PackageManager;

final class Notices {
    static Notification create(Context c,String text){
        NotificationManager manager=c.getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel("sync","剪贴板同步结果",NotificationManager.IMPORTANCE_LOW));
        PendingIntent open=PendingIntent.getActivity(c,0,new Intent(c,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(c,"sync").setSmallIcon(android.R.drawable.ic_menu_share).setContentTitle("剪贴板同步").setContentText(text).setContentIntent(open).setAutoCancel(true).setVisibility(Notification.VISIBILITY_PRIVATE).build();
    }
    static void show(Context c,String text){
        Config.status(c,text);
        if(Build.VERSION.SDK_INT<33||c.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)==PackageManager.PERMISSION_GRANTED)c.getSystemService(NotificationManager.class).notify(1,create(c,text));
    }
}
