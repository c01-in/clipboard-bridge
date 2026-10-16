package com.colin.clipbridge;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.*;
import android.widget.*;
import java.util.*;
import java.util.concurrent.*;

public class MainActivity extends Activity {
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final ExecutorService sender=Executors.newSingleThreadExecutor();
    private TextView status,sendStatus,discoveryStatus;
    private Button sendButton;
    private LinearLayout computers;
    private AutoPairing pairing;
    private boolean visible;
    private int discoveryGeneration;
    private final Runnable refresh=new Runnable(){public void run(){if(visible){status.setText(Config.statusText(MainActivity.this));handler.postDelayed(this,1500);}}};
    private Button button(LinearLayout parent,int text,Runnable action){Button b=new Button(this);b.setText(text);b.setAllCaps(false);b.setOnClickListener(v->action.run());parent.addView(b,new LinearLayout.LayoutParams(-1,-2));return b;}
    private TextView text(LinearLayout parent,int resource,int size){TextView t=new TextView(this);t.setText(resource);t.setTextSize(size);t.setPadding(0,12,0,12);parent.addView(t);return t;}
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);
        ScrollView scroll=new ScrollView(this);LinearLayout layout=new LinearLayout(this);layout.setOrientation(LinearLayout.VERTICAL);
        int pad=(int)(24*getResources().getDisplayMetrics().density);layout.setPadding(pad,pad,pad,pad);scroll.addView(layout);setContentView(scroll);
        text(layout,R.string.app_name,29);text(layout,R.string.app_description,16);
        status=text(layout,R.string.status_unpaired,17);
        sendButton=button(layout,R.string.send_clipboard,this::sendClipboard);
        sendStatus=text(layout,R.string.send_help,15);
        text(layout,R.string.connection_heading,21);text(layout,R.string.discovery_help,15);
        discoveryStatus=text(layout,R.string.discovery_searching,16);
        computers=new LinearLayout(this);computers.setOrientation(LinearLayout.VERTICAL);layout.addView(computers);
        button(layout,R.string.find_computer,()->startDiscovery(true));
        button(layout,R.string.check_connection,()->{RegistrationWorker.schedule(this);Config.status(this,R.string.status_checking);startDiscovery(false);});
        text(layout,R.string.notification_help,14);
        button(layout,R.string.system_settings,()->startActivity(new Intent(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+getPackageName()))));
        ArrayList<String> permissions=new ArrayList<>();
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED)permissions.add(Manifest.permission.POST_NOTIFICATIONS);
        if(Build.VERSION.SDK_INT>=37&&checkSelfPermission("android.permission.ACCESS_LOCAL_NETWORK")!=PackageManager.PERMISSION_GRANTED)permissions.add("android.permission.ACCESS_LOCAL_NETWORK");
        if(!permissions.isEmpty())requestPermissions(permissions.toArray(new String[0]),1);
    }
    private void startDiscovery(boolean replace){
        final int generation=++discoveryGeneration;
        if(pairing!=null)pairing.close();computers.removeAllViews();
        discoveryStatus.setText(replace?R.string.discovery_searching:R.string.discovery_reconnecting);
        pairing=new AutoPairing(this,replace,new AutoPairing.Listener(){
            public void status(int resource,Object... args){runOnUiThread(()->{if(visible&&generation==discoveryGeneration)discoveryStatus.setText(getString(resource,args));});}
            public void found(AutoPairing.Computer pc){runOnUiThread(()->{
                if(!visible||generation!=discoveryGeneration)return;
                LinearLayout card=new LinearLayout(MainActivity.this);card.setOrientation(LinearLayout.VERTICAL);card.setPadding(0,12,0,12);
                TextView name=new TextView(MainActivity.this);name.setText(pc.name);name.setTextSize(20);card.addView(name);
                TextView code=new TextView(MainActivity.this);code.setText(getString(R.string.confirmation_code,pc.code));code.setTextSize(24);card.addView(code);
                button(card,R.string.confirm_pair,()->new AlertDialog.Builder(MainActivity.this).setTitle(R.string.confirm_pair)
                    .setMessage(getString(R.string.confirm_pair_message,pc.name,pc.code)).setNegativeButton(R.string.cancel,null)
                    .setPositiveButton(R.string.confirm,(d,w)->{if(pairing!=null)pairing.confirm(pc);}).show());
                computers.addView(card);discoveryStatus.setText(R.string.pair_compare);
            });}
            public void paired(){runOnUiThread(()->{if(visible&&generation==discoveryGeneration){computers.removeAllViews();discoveryStatus.setText(R.string.status_paired);startDiscovery(false);}});}
        });
        pairing.start();
    }
    private void sendClipboard(){
        try {
            Config.read(this);ClipboardSender.Input input=ClipboardSender.capture(this);
            sendButton.setEnabled(false);sendStatus.setText(R.string.send_wait);
            Context context=getApplicationContext();sender.execute(()->{
                String result;
                try{ClipboardSender.send(context,input);result=getString(R.string.send_success);}
                catch(OutOfMemoryError e){result=getString(R.string.error_image_memory);}
                catch(Exception e){result=getString(R.string.send_failed,e.getMessage());}
                final String message=result;runOnUiThread(()->{if(!isDestroyed()){sendStatus.setText(message);sendButton.setEnabled(true);}});
            });
        }catch(Exception e){sendStatus.setText(getString(R.string.send_failed,e.getMessage()));}
    }
    @Override protected void onResume(){super.onResume();visible=true;handler.post(refresh);startDiscovery(!Config.prefs(this).contains("config"));}
    @Override protected void onPause(){visible=false;++discoveryGeneration;handler.removeCallbacks(refresh);if(pairing!=null){pairing.close();pairing=null;}super.onPause();}
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] grants){super.onRequestPermissionsResult(request,permissions,grants);if(visible)startDiscovery(!Config.prefs(this).contains("config"));}
    @Override protected void onDestroy(){if(pairing!=null)pairing.close();sender.shutdown();super.onDestroy();}
}
