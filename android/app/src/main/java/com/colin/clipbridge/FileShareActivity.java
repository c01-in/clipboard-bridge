package com.colin.clipbridge;

import android.app.Activity;
import android.content.Intent;
import android.os.*;
import android.widget.*;
import java.util.ArrayList;
import android.net.Uri;

public final class FileShareActivity extends Activity {
    private final Handler handler=new Handler(Looper.getMainLooper());
    private TextView status,name;
    private ProgressBar progress;
    private Button close;
    private String jobId;
    private boolean visible;
    private final Runnable refresh=new Runnable(){public void run(){if(visible){render();handler.postDelayed(this,300);}}};
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);setTitle(R.string.send_to_computer);
        LinearLayout layout=new LinearLayout(this);layout.setOrientation(LinearLayout.VERTICAL);int pad=(int)(24*getResources().getDisplayMetrics().density);layout.setPadding(pad,pad,pad,pad);
        status=new TextView(this);status.setTextSize(18);layout.addView(status);
        name=new TextView(this);layout.addView(name);
        progress=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);progress.setMax(1000);layout.addView(progress,new LinearLayout.LayoutParams(-1,-2));
        close=new Button(this);close.setText(android.R.string.ok);layout.addView(close);close.setOnClickListener(v->{FileSendService.State job=FileSendService.state;if(job!=null&&job.id.equals(jobId)&&job.running)job.cancelled.set(true);else finish();});setContentView(layout);
        jobId=getIntent().getStringExtra("jobId");
        if(jobId==null){jobId=FileTransfer.id();getIntent().putExtra("jobId",jobId);}
        if(saved==null&&(FileSendService.state==null||!FileSendService.state.id.equals(jobId))){
            if(FileSendService.state!=null&&FileSendService.state.running){status.setText(R.string.files_busy);close.setText(android.R.string.ok);return;}
            try{
                ArrayList<Uri> uris=ShareInput.files(getIntent());
                if(uris.isEmpty())throw new Exception(getString(R.string.error_share_content));
                Intent service=new Intent(this,FileSendService.class).putExtra("jobId",jobId).putParcelableArrayListExtra(Intent.EXTRA_STREAM,uris).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                service.setClipData(ShareInput.grants(uris));
                startForegroundService(service);
            }catch(Exception e){status.setText(getString(R.string.send_failed,e.getMessage()));close.setText(android.R.string.ok);}
        }
    }
    private void render(){
        FileSendService.State job=FileSendService.state;
        if(job==null||!job.id.equals(jobId)){if(status.length()==0)status.setText(R.string.files_interrupted);return;}
        close.setText(job.running?R.string.cancel:android.R.string.ok);
        if(!job.running){status.setText(job.success?getString(R.string.files_sent):getString(R.string.send_failed,job.error));progress.setIndeterminate(false);if(job.success)progress.setProgress(1000);return;}
        name.setText(job.name);
        status.setText(job.saving?getString(R.string.files_saving_pc):getString(R.string.files_count,String.valueOf(Math.min(job.index+1,job.count)),String.valueOf(job.count))+"\n"+getString(R.string.files_progress,format(job.bytes),job.total<0?"?":format(job.total)));
        progress.setIndeterminate(job.total<0||job.saving);
        if(job.total>0)progress.setProgress((int)Math.min(1000,(double)job.bytes/job.total*1000));
    }
    static String format(long bytes){return String.format(java.util.Locale.getDefault(),"%.1f MiB",bytes/(1024d*1024));}
    @Override protected void onResume(){super.onResume();visible=true;handler.post(refresh);}
    @Override protected void onPause(){visible=false;handler.removeCallbacks(refresh);super.onPause();}
}
