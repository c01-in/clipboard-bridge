package com.colin.clipbridge;

import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.net.Uri;
import android.os.*;
import java.util.ArrayList;
import java.util.concurrent.atomic.AtomicBoolean;
import androidx.core.content.IntentCompat;

/** Foreground only while a user-requested file transfer is active; never a clipboard listener. */
public final class FileSendService extends Service {
    static final int NOTICE=20;
    static final class State {
        final String id;final AtomicBoolean cancelled=new AtomicBoolean();
        volatile boolean running=true,success,saving;
        volatile int index,count;volatile long bytes,total;volatile String name="",error="";
        State(String id){this.id=id;}
    }
    static volatile State state;
    private int latestStartId;
    @Override public IBinder onBind(Intent intent){return null;}
    @Override public int onStartCommand(Intent intent,int flags,int startId){
        latestStartId=startId;
        if(intent==null){stopSelf(startId);return START_NOT_STICKY;}
        if("cancel".equals(intent.getAction())){
            State s=state;
            if(s!=null&&s.running){if(s.id.equals(intent.getStringExtra("jobId")))s.cancelled.set(true);}
            else stopSelf(startId);
            return START_NOT_STICKY;
        }
        if(state!=null&&state.running)return START_NOT_STICKY;
        State job=new State(intent.getStringExtra("jobId"));state=job;
        Notification notification=notice(job);
        if(Build.VERSION.SDK_INT>=29)startForeground(NOTICE,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);else startForeground(NOTICE,notification);
        ArrayList<Uri> uris=IntentCompat.getParcelableArrayListExtra(intent,Intent.EXTRA_STREAM,Uri.class);
        new Thread(()->{
            try{
                if(uris==null)throw new Exception(getString(R.string.error_share_content));
                final long[] last={0};
                FileTransfer.send(this,uris,job.cancelled::get,(index,count,bytes,total,name,saving)->{
                    job.index=index;job.count=count;job.bytes=bytes;job.total=total;job.name=name;job.saving=saving;
                    if(SystemClock.elapsedRealtime()-last[0]>=500||saving){last[0]=SystemClock.elapsedRealtime();getSystemService(NotificationManager.class).notify(NOTICE,notice(job));}
                });
                job.success=true;
            }catch(Exception e){job.error=e.getMessage()==null?e.getClass().getSimpleName():e.getMessage();}
            finally{
                new Handler(Looper.getMainLooper()).post(()->{
                    job.running=false;stopForeground(STOP_FOREGROUND_REMOVE);
                    String result=job.success?getString(R.string.files_sent):getString(R.string.send_failed,job.error);
                    Config.status(this,job.success?R.string.files_sent:R.string.send_failed,job.error);
                    if(Build.VERSION.SDK_INT<33||checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)==android.content.pm.PackageManager.PERMISSION_GRANTED)
                        getSystemService(NotificationManager.class).notify(21,Notices.create(this,result));
                    // A notification Cancel intent also receives a new startId.
                    stopSelf(latestStartId);
                });
            }
        },"ClipboardBridge-file-send").start();
        return START_NOT_STICKY;
    }
    private Notification notice(State job){
        NotificationManager manager=getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel("files",getString(R.string.files_channel),NotificationManager.IMPORTANCE_LOW));
        Intent open=new Intent(this,FileShareActivity.class).putExtra("jobId",job.id);
        PendingIntent content=PendingIntent.getActivity(this,20,open,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        PendingIntent cancel=PendingIntent.getService(this,21,new Intent(this,FileSendService.class).setAction("cancel").putExtra("jobId",job.id),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        int amount=job.total<=0?0:(int)Math.min(1000,(double)job.bytes/job.total*1000);
        return new Notification.Builder(this,"files").setSmallIcon(android.R.drawable.ic_menu_upload).setContentTitle(getString(R.string.app_name))
            .setContentText(job.count==0?getString(R.string.send_wait):job.saving?getString(R.string.files_saving_pc):getString(R.string.files_count,String.valueOf(Math.min(job.index+1,job.count)),String.valueOf(job.count))+" · "+job.name)
            .setProgress(1000,amount,job.total<0||job.saving).setOngoing(true).setContentIntent(content).setVisibility(Notification.VISIBILITY_PRIVATE)
            .addAction(new Notification.Action.Builder(null,getString(R.string.cancel),cancel).build()).build();
    }
    @Override public void onDestroy(){State job=state;if(job!=null&&job.running)job.cancelled.set(true);super.onDestroy();}
    @Override public void onTimeout(int startId,int fgsType){State job=state;if(job!=null){job.cancelled.set(true);job.error=getString(R.string.files_interrupted);job.running=false;}stopForeground(STOP_FOREGROUND_REMOVE);stopSelf();}
}
