package com.colin.clipbridge;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Receives Android share/open-with requests for the already paired computer. */
public final class ShareActivity extends Activity {
    private Transfer transfer;
    private TextView status;
    private Button retry,settings,close;
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);
        try{
            if(ShareInput.asFiles(getIntent())){
                Intent files=ShareInput.fileIntent(getIntent()).setClass(this,FileShareActivity.class);
                startActivity(files);finish();return;
            }
        }catch(Exception ignored){/* Display a normal, actionable share validation error below. */}
        setTitle(R.string.send_to_computer);
        LinearLayout layout=new LinearLayout(this);layout.setOrientation(LinearLayout.VERTICAL);
        int pad=(int)(24*getResources().getDisplayMetrics().density);layout.setPadding(pad,pad,pad,pad);
        status=new TextView(this);status.setTextSize(17);layout.addView(status);
        retry=new Button(this);retry.setText(R.string.share_retry);retry.setAllCaps(false);retry.setOnClickListener(v->transfer.start());layout.addView(retry);
        settings=new Button(this);settings.setText(R.string.share_open_settings);settings.setAllCaps(false);
        settings.setOnClickListener(v->{startActivity(new Intent(this,MainActivity.class));finish();});layout.addView(settings);
        close=new Button(this);close.setText(android.R.string.ok);close.setOnClickListener(v->finish());layout.addView(close);
        setContentView(layout);
        transfer=(Transfer)getLastNonConfigurationInstance();
        if(transfer==null){
            transfer=new Transfer(getApplicationContext(),new Intent(getIntent()));
            // A process recreation must not silently send the same share a second time.
            if(saved==null&&!transfer.manual)transfer.start();
            else if(saved==null)transfer.message=getString(R.string.send_help);
            else transfer.message=getString(R.string.share_interrupted);
        }
        transfer.changed=this::render;render();
    }
    private void render(){
        if(isDestroyed()||isFinishing())return;
        status.setText(transfer.message);
        retry.setText(transfer.manual?R.string.send_clipboard:R.string.share_retry);
        retry.setVisibility(transfer.busy||transfer.success?View.GONE:View.VISIBLE);
        settings.setVisibility(transfer.busy||transfer.success?View.GONE:View.VISIBLE);
        close.setVisibility(transfer.busy?View.GONE:View.VISIBLE);
        if(transfer.success){setResult(RESULT_OK);new Handler(Looper.getMainLooper()).postDelayed(()->{if(!isDestroyed())finish();},1200);}
    }
    @Override public Object onRetainNonConfigurationInstance(){return transfer;}
    @Override protected void onDestroy(){if(transfer!=null)transfer.changed=null;super.onDestroy();}

    private static final class Transfer {
        final Context context;
        final Intent intent;
        final Handler handler=new Handler(Looper.getMainLooper());
        Runnable changed;
        boolean busy,success;
        final boolean manual;
        String message;
        Transfer(Context context,Intent intent){this.context=context;this.intent=intent;manual="text/plain".equals(intent.getType())&&!intent.hasExtra(Intent.EXTRA_STREAM);message=context.getString(R.string.send_wait);}
        void start(){
            if(busy||success)return;
            busy=true;message=context.getString(R.string.send_wait);if(changed!=null)changed.run();
            ExecutorService executor=Executors.newSingleThreadExecutor();
            executor.execute(()->{
                String result;boolean applied=false;
                try{
                    Config.read(context);
                    if(SyncState.paused(context))throw new Exception(context.getString(R.string.sync_paused));
                    if(!SyncState.ready(context))throw new Exception(context.getString(R.string.sync_restoring));
                    ClipboardSender.send(context,ShareInput.capture(intent));
                    applied=true;result=context.getString(R.string.send_success);
                }catch(OutOfMemoryError e){result=context.getString(R.string.error_image_memory);}
                catch(Exception e){result=context.getString(R.string.send_failed,e.getMessage());}
                final String text=result;final boolean completed=applied;
                handler.post(()->{busy=false;success=completed;message=text;if(changed!=null)changed.run();});
            });
            executor.shutdown();
        }
    }
}
