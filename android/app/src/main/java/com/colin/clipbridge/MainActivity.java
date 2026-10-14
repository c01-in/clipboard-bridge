package com.colin.clipbridge;
import android.Manifest;
import android.app.*;
import android.content.*;
import android.net.Uri;
import android.os.*;
import android.util.Base64;
import android.view.*;
import android.widget.*;
import org.json.JSONObject;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends Activity {
    TextView status;EditText link;Handler handler=new Handler(Looper.getMainLooper());
    final ExecutorService sender=Executors.newSingleThreadExecutor();
    TextView sendStatus;Button sendButton;
    final Runnable refresh=new Runnable(){public void run(){if(status!=null)status.setText(Config.prefs(MainActivity.this).getString("status","尚未配对"));handler.postDelayed(this,1500);}};
    @Override public void onCreate(Bundle saved){super.onCreate(saved);
        ScrollView scroll=new ScrollView(this);LinearLayout layout=new LinearLayout(this);layout.setOrientation(LinearLayout.VERTICAL);int pad=(int)(24*getResources().getDisplayMetrics().density);layout.setPadding(pad,pad,pad,pad);scroll.addView(layout);setContentView(scroll);
        TextView title=new TextView(this);title.setText("剪贴板同步");title.setTextSize(30);layout.addView(title);
        TextView description=new TextView(this);description.setText("Windows 复制，手机直接粘贴。\n手机复制后，点击下方按钮发送到 Windows。\n通过 FCM 按需接收，无常驻服务。\n反向发送、图片／长文字需要局域网或 Tailscale 能连接电脑。");description.setTextSize(16);description.setPadding(0,pad,0,pad);layout.addView(description);
        status=new TextView(this);status.setTextSize(17);layout.addView(status);
        sendButton=new Button(this);sendButton.setText("发送剪贴板到 Windows");layout.addView(sendButton);sendButton.setOnClickListener(v->sendClipboard());
        sendStatus=new TextView(this);sendStatus.setText(Config.prefs(this).getString("lastSendStatus","支持文字和单张图片；只在点击时读取剪贴板。"));sendStatus.setTextSize(16);sendStatus.setPadding(0,0,0,pad);layout.addView(sendStatus);
        link=new EditText(this);link.setHint("粘贴 Windows 托盘程序生成的配对链接");link.setMinLines(3);link.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE);layout.addView(link);
        Button pair=new Button(this);pair.setText("配对并启用同步");layout.addView(pair);pair.setOnClickListener(v->pair(link.getText().toString().trim()));
        Button retry=new Button(this);retry.setText("重新注册推送／检查连接");layout.addView(retry);retry.setOnClickListener(v->{RegistrationWorker.schedule(this);Config.status(this,"正在检查 Google 推送和电脑连接…");});
        TextView help=new TextView(this);help.setText("首次使用请允许通知。三星系统若限制了后台运行，请在 App 电池设置中解除限制。\n\n同步结果会显示一次普通通知，不会持续驻留。FCM 不能保证每次即时送达。\n\n图片通过系统图片剪贴板提供；目标 App 必须支持图片粘贴。");help.setPadding(0,pad,0,pad);layout.addView(help);
        Button settings=new Button(this);settings.setText("打开本应用系统设置");layout.addView(settings);settings.setOnClickListener(v->startActivity(new Intent(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+getPackageName()))));
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED)requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},1);
        handleIntent(getIntent());
    }
    @Override protected void onNewIntent(Intent i){super.onNewIntent(i);setIntent(i);handleIntent(i);}
    void handleIntent(Intent i){if(i!=null&&i.getData()!=null&&"clipbridge".equals(i.getData().getScheme())){link.setText(i.getData().toString());}}
    void pair(String value){try{
        Uri uri=Uri.parse(value);if(!"clipbridge".equals(uri.getScheme())||!"pair".equals(uri.getHost()))throw new Exception("配对链接格式错误");
        JSONObject config=new JSONObject(new String(Base64.decode(uri.getQueryParameter("data"),Base64.NO_WRAP),StandardCharsets.UTF_8));
        if(Base64.decode(config.getString("key"),Base64.NO_WRAP).length!=32||config.getJSONArray("endpoints").length()==0)throw new Exception("配对数据无效");
        new AlertDialog.Builder(this).setTitle("确认配对电脑").setMessage("仅在这是你自己的电脑时继续：\n"+config.getJSONArray("endpoints").toString()).setNegativeButton("取消",null).setPositiveButton("配对",(d,w)->{try{Config.save(this,config);for(com.google.firebase.FirebaseApp app:com.google.firebase.FirebaseApp.getApps(this))app.delete();Config.initialize(this);RegistrationWorker.schedule(this);link.setText("");Config.status(this,"配置已加密保存，正在注册推送…");}catch(Exception e){Config.status(this,"配对失败："+e.getMessage());}}).show();
    }catch(Exception e){Config.status(this,"配对失败："+e.getMessage());}}
    void sendClipboard(){
        try{
            Config.read(this);
            ClipboardSender.Input input=ClipboardSender.capture(this);
            sendButton.setEnabled(false);sendStatus.setText("正在发送，等待 Windows 确认写入…");
            Context context=getApplicationContext();
            sender.execute(()->{
                String result;
                try{ClipboardSender.send(context,input);result="Windows 已确认写入剪贴板，可以粘贴。";}
                catch(OutOfMemoryError e){result="图片过大，内存不足，未确认发送；请复制较小的图片。";}
                catch(Exception e){result="发送失败："+e.getMessage();}
                final String message=result;
                Config.prefs(context).edit().putString("lastSendStatus",message).apply();
                runOnUiThread(()->{if(!isDestroyed()){sendStatus.setText(message);sendButton.setEnabled(true);}});
            });
        }catch(Exception e){sendStatus.setText("发送失败："+e.getMessage());}
    }
    @Override protected void onResume(){super.onResume();handler.post(refresh);}
    @Override protected void onPause(){handler.removeCallbacks(refresh);super.onPause();}
    @Override protected void onDestroy(){sender.shutdown();super.onDestroy();}
}
