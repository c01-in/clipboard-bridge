package com.colin.clipbridge;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.os.*;
import android.widget.*;
import java.util.concurrent.*;

public class MainActivity extends Activity {
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final ExecutorService sender=Executors.newSingleThreadExecutor();
    private TextView status,sendStatus,discoveryStatus;
    private Button sendButton;
    private Switch syncSwitch;
    private boolean updatingSwitch;
    private LinearLayout computers,connection,permissionCard;
    private boolean choosingComputer,permissionDialog;
    private AutoPairing pairing;
    private boolean visible,sending;
    private int discoveryGeneration;
    private final Runnable refresh=new Runnable(){public void run(){if(visible){refreshSync();handler.postDelayed(this,1500);}}};
    private void refreshSync(){
        Notices.dismissViewed(this);
        status.setText(Config.statusText(this));updatingSwitch=true;syncSwitch.setChecked(!SyncState.paused(this));updatingSwitch=false;
        if(SyncState.paused(this))sendStatus.setText(R.string.sync_pause_help);
        if(!sending)sendButton.setEnabled(Config.prefs(this).contains("config")&&SyncState.ready(this));
    }
    private Button button(LinearLayout parent,int text,Runnable action){Button b=new Button(this);b.setText(text);b.setAllCaps(false);b.setOnClickListener(v->action.run());parent.addView(b,new LinearLayout.LayoutParams(-1,-2));return b;}
    private TextView text(LinearLayout parent,int resource,int size){TextView t=new TextView(this);t.setText(resource);t.setTextSize(size);t.setPadding(0,12,0,12);parent.addView(t);return t;}
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);
        ScrollView scroll=new ScrollView(this);LinearLayout layout=new LinearLayout(this);layout.setOrientation(LinearLayout.VERTICAL);
        int pad=(int)(24*getResources().getDisplayMetrics().density);layout.setPadding(pad,pad,pad,pad);scroll.addView(layout);setContentView(scroll);
        LinearLayout heading=new LinearLayout(this);heading.setGravity(android.view.Gravity.CENTER_VERTICAL);layout.addView(heading);
        TextView title=new TextView(this);title.setText(R.string.app_name);title.setTextSize(27);heading.addView(title,new LinearLayout.LayoutParams(0,-2,1));
        Button more=new Button(this);more.setText("⋮");more.setContentDescription(getString(R.string.more_options));heading.addView(more,new LinearLayout.LayoutParams(-2,-2));more.setOnClickListener(v->showMenu(more));
        status=text(layout,R.string.status_unpaired,17);
        syncSwitch=new Switch(this);syncSwitch.setText(R.string.sync_enabled);layout.addView(syncSwitch,new LinearLayout.LayoutParams(-1,-2));
        syncSwitch.setOnCheckedChangeListener((view,enabled)->{
            if(updatingSwitch)return;
            try{SyncState.setPaused(this,!enabled);sendStatus.setText(enabled?R.string.send_help:R.string.sync_pause_help);}
            catch(Exception e){sendStatus.setText(getString(R.string.send_failed,e.getMessage()));}
            refreshSync();
        });
        permissionCard=new LinearLayout(this);permissionCard.setOrientation(LinearLayout.VERTICAL);layout.addView(permissionCard);
        text(permissionCard,R.string.notification_required,15);button(permissionCard,R.string.enable_notifications,()->requestNotifications(true));
        sendButton=button(layout,R.string.send_clipboard,this::sendClipboard);
        button(layout,R.string.files_clipboard_image,()->pickFiles(true));
        button(layout,R.string.files_choose,()->pickFiles(false));
        sendStatus=text(layout,R.string.send_help,15);
        connection=new LinearLayout(this);connection.setOrientation(LinearLayout.VERTICAL);layout.addView(connection);
        text(connection,R.string.connection_heading,21);text(connection,R.string.discovery_help,15);
        button(connection,R.string.download_windows,this::showWindowsDownload);
        discoveryStatus=text(connection,R.string.discovery_searching,16);
        computers=new LinearLayout(this);computers.setOrientation(LinearLayout.VERTICAL);connection.addView(computers);
    }
    private void showMenu(android.view.View anchor){
        PopupMenu menu=new PopupMenu(this,anchor);
        menu.getMenu().add(0,1,0,R.string.change_computer);menu.getMenu().add(0,2,1,R.string.reconnect);menu.getMenu().add(0,6,2,R.string.files_choose_receive_folder);menu.getMenu().add(0,4,3,R.string.download_windows);menu.getMenu().add(0,5,4,R.string.privacy_policy);menu.getMenu().add(0,3,5,R.string.about);
        menu.setOnMenuItemClickListener(item->{
            if(item.getItemId()==1){choosingComputer=true;startDiscovery(true);}
            else if(item.getItemId()==2){choosingComputer=false;if(Config.prefs(this).contains("config"))PushRegistration.refresh(this);startDiscovery(!Config.prefs(this).contains("config"));}
            else if(item.getItemId()==4)showWindowsDownload();
            else if(item.getItemId()==6)startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION),43);
            else if(item.getItemId()==5){try{startActivity(new Intent(Intent.ACTION_VIEW,android.net.Uri.parse(CompanionLinks.PRIVACY)));}catch(ActivityNotFoundException e){Toast.makeText(this,R.string.link_unavailable,Toast.LENGTH_LONG).show();}}
            else {String version;try{version=getPackageManager().getPackageInfo(getPackageName(),0).versionName;}catch(PackageManager.NameNotFoundException e){throw new IllegalStateException(e);}
                new AlertDialog.Builder(this).setTitle(R.string.about).setMessage(getString(R.string.about_version,version)+"\n\n"+getString(R.string.notification_help)).setPositiveButton(android.R.string.ok,null).show();}
            return true;
        });menu.show();
    }
    private boolean notificationsAllowed(){NotificationManager manager=getSystemService(NotificationManager.class);NotificationChannel channel=manager.getNotificationChannel("sync");return manager.areNotificationsEnabled()&&(channel==null||channel.getImportance()!=NotificationManager.IMPORTANCE_NONE);}
    private void showWindowsDownload(){
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(R.string.download_windows)
            .setMessage(getString(R.string.windows_download_help,CompanionLinks.WINDOWS))
            .setPositiveButton(R.string.copy_link,(d,w)->{
                getSystemService(ClipboardManager.class).setPrimaryClip(ClipData.newPlainText(getString(R.string.download_windows),CompanionLinks.WINDOWS));
                if(Build.VERSION.SDK_INT<33)Toast.makeText(this,R.string.link_copied,Toast.LENGTH_SHORT).show();
            }).setNeutralButton(R.string.share_link,(d,w)->{
                Intent share=new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT,CompanionLinks.WINDOWS);
                try{startActivity(Intent.createChooser(share,getString(R.string.download_windows)));}catch(ActivityNotFoundException e){Toast.makeText(this,R.string.link_unavailable,Toast.LENGTH_LONG).show();}
            }).setNegativeButton(android.R.string.cancel,null).create();
        dialog.show();
        TextView message=dialog.findViewById(android.R.id.message);
        if(message!=null){android.text.util.Linkify.addLinks(message,android.text.util.Linkify.WEB_URLS);message.setMovementMethod(android.text.method.LinkMovementMethod.getInstance());}
    }
    private void updatePermissionCard(){permissionCard.setVisibility(notificationsAllowed()?android.view.View.GONE:android.view.View.VISIBLE);}
    private void requestNotifications(boolean userInitiated){
        if(notificationsAllowed()||permissionDialog||isFinishing())return;
        boolean asked=Config.prefs(this).getBoolean("notificationPermissionRequested",false);
        if(!userInitiated&&Config.prefs(this).getBoolean("notificationIntroShown",false))return;
        permissionDialog=true;Config.prefs(this).edit().putBoolean("notificationIntroShown",true).apply();
        boolean canRequest=Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED&&(!asked||shouldShowRequestPermissionRationale(Manifest.permission.POST_NOTIFICATIONS));
        new AlertDialog.Builder(this).setTitle(R.string.notification_required).setMessage(canRequest?R.string.permission_intro:R.string.permission_settings_needed)
            .setNegativeButton(R.string.cancel,null).setPositiveButton(canRequest?R.string.continue_permission:R.string.system_settings,(d,w)->{
                if(canRequest){Config.prefs(this).edit().putBoolean("notificationPermissionRequested",true).apply();requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},1);}
                else {NotificationManager manager=getSystemService(NotificationManager.class);NotificationChannel channel=manager.getNotificationChannel("sync");
                    Intent settings=new Intent(manager.areNotificationsEnabled()&&channel!=null&&channel.getImportance()==NotificationManager.IMPORTANCE_NONE?android.provider.Settings.ACTION_CHANNEL_NOTIFICATION_SETTINGS:android.provider.Settings.ACTION_APP_NOTIFICATION_SETTINGS);
                    settings.putExtra(android.provider.Settings.EXTRA_APP_PACKAGE,getPackageName()).putExtra(android.provider.Settings.EXTRA_CHANNEL_ID,"sync");startActivity(settings);}
            }).setOnDismissListener(d->permissionDialog=false).show();
    }
    private void startDiscovery(boolean replace){
        final int generation=++discoveryGeneration;
        if(pairing!=null)pairing.close();computers.removeAllViews();
        connection.setVisibility(replace||!Config.prefs(this).contains("config")?android.view.View.VISIBLE:android.view.View.GONE);
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
            public void paired(){runOnUiThread(()->{if(visible&&generation==discoveryGeneration){choosingComputer=false;computers.removeAllViews();discoveryStatus.setText(R.string.status_paired);startDiscovery(false);}});}
        });
        pairing.start();
    }
    private void sendClipboard(){
        try {
            Config.read(this);ClipboardSender.Input input=ClipboardSender.capture(this);
            sending=true;sendButton.setEnabled(false);sendStatus.setText(R.string.send_wait);
            Context context=getApplicationContext();sender.execute(()->{
                String result;
                try{ClipboardSender.send(context,input);result=getString(R.string.send_success);}
                catch(OutOfMemoryError e){result=getString(R.string.error_image_memory);}
                catch(Exception e){result=getString(R.string.send_failed,e.getMessage());}
                final String message=result;runOnUiThread(()->{sending=false;if(!isDestroyed()){sendStatus.setText(message);refreshSync();}});
            });
        }catch(Exception e){sendStatus.setText(getString(R.string.send_failed,e.getMessage()));}
    }
    private void pickFiles(boolean clipboard){
        if(!SyncState.ready(this)){sendStatus.setText(R.string.sync_paused);return;}
        Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT).setType(clipboard?"image/*":"*/*").addCategory(Intent.CATEGORY_OPENABLE)
            .putExtra(Intent.EXTRA_ALLOW_MULTIPLE,!clipboard).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try{startActivityForResult(intent,clipboard?41:42);}catch(ActivityNotFoundException e){sendStatus.setText(R.string.files_picker_unavailable);}
    }
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);
        if(request==43&&result==RESULT_OK&&data!=null&&data.getData()!=null){
            try{
                int required=Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION;
                if((data.getFlags()&required)!=required)throw new Exception(getString(R.string.files_folder_invalid));
                getContentResolver().takePersistableUriPermission(data.getData(),Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
                Config.prefs(this).edit().putString("receiveFolder",data.getData().toString()).apply();sendStatus.setText(R.string.files_folder_saved);
            }
            catch(Exception e){sendStatus.setText(getString(R.string.files_save_failed,e.getMessage()));}return;
        }
        if((request!=41&&request!=42)||result!=RESULT_OK||data==null)return;
        java.util.ArrayList<android.net.Uri> uris=new java.util.ArrayList<>();
        if(data.getClipData()!=null){for(int i=0;i<data.getClipData().getItemCount();i++)uris.add(data.getClipData().getItemAt(i).getUri());}
        else if(data.getData()!=null)uris.add(data.getData());
        if(uris.isEmpty())return;
        if(request==41&&uris.size()!=1){sendStatus.setText(R.string.error_one_item);return;}
        Intent share=new Intent(this,ShareActivity.class).setAction(request==41?Intent.ACTION_SEND:Intent.ACTION_SEND_MULTIPLE)
            .setType(request==41?"image/*":"*/*").putExtra("asFiles",request==42).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        ClipData clip=ClipData.newRawUri("",uris.get(0));for(int i=1;i<uris.size();i++)clip.addItem(new ClipData.Item(uris.get(i)));share.setClipData(clip);
        if(request==41)share.putExtra(Intent.EXTRA_STREAM,uris.get(0));else share.putParcelableArrayListExtra(Intent.EXTRA_STREAM,uris);
        startActivity(share);
    }
    @Override protected void onResume(){super.onResume();visible=true;handler.post(refresh);updatePermissionCard();if(Config.prefs(this).contains("config")&&!SyncState.ready(this))RegistrationWorker.schedule(this);startDiscovery(choosingComputer||!Config.prefs(this).contains("config"));handler.post(()->{if(visible)requestNotifications(false);});}
    @Override protected void onPause(){visible=false;++discoveryGeneration;handler.removeCallbacks(refresh);if(pairing!=null){pairing.close();pairing=null;}super.onPause();}
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] grants){super.onRequestPermissionsResult(request,permissions,grants);updatePermissionCard();if(visible)startDiscovery(choosingComputer||!Config.prefs(this).contains("config"));}
    @Override protected void onDestroy(){if(pairing!=null)pairing.close();sender.shutdown();super.onDestroy();}
}
