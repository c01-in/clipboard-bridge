package com.colin.clipbridge.webpushprobe;

import android.Manifest;
import android.app.Activity;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.os.Bundle;
import android.os.Handler;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import org.json.JSONObject;
import org.unifiedpush.android.connector.UnifiedPush;

public final class MainActivity extends Activity {
    private final Handler handler = new Handler();
    private TextView status;
    private final Runnable refresh = new Runnable() {
        public void run() {
            status.setText(getSharedPreferences("probe", 0).getString("status", "尚未注册"));
            handler.postDelayed(this, 1000);
        }
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        getSystemService(NotificationManager.class).createNotificationChannel(
            new NotificationChannel("probe", "实际收到的测试推送", NotificationManager.IMPORTANCE_DEFAULT));
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL);
        int pad = (int)(24*getResources().getDisplayMetrics().density);root.setPadding(pad,pad,pad,pad);
        TextView title = new TextView(this); title.setText("Web Push 独立验证");title.setTextSize(24);root.addView(title);
        TextView description = new TextView(this);
        description.setText("独立包名，不读写剪贴板，不使用 Firebase 项目或发送私钥。\n仅验证 Windows → Google → 原生 APK。没有自建服务器或常驻接收服务。\n\n");root.addView(description);
        status = new TextView(this);status.setTextIsSelectable(true);root.addView(status);
        Button register = new Button(this);register.setText("注册／重试");register.setOnClickListener(v->register());root.addView(register);
        setContentView(root);
        String key=getIntent().getStringExtra("vapid");
        if(key!=null && key.matches("[A-Za-z0-9_-]{87}"))getSharedPreferences("probe",0).edit().putString("vapid",key).apply();
        if(android.os.Build.VERSION.SDK_INT>=33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},1);
        register();
    }
    private void register() {
        try {
            String key=getSharedPreferences("probe",0).getString("vapid",null);
            if(key==null)throw new IllegalStateException("等待 Windows 导入本机 VAPID 公钥");
            UnifiedPush.saveDistributor(this,getPackageName());
            UnifiedPush.register(this,"probe","Windows direct Web Push probe",key);
            ProbeState.event(this,"registration-requested",new JSONObject());
        } catch(Exception e){try{ProbeState.event(this,"registration-error",new JSONObject().put("error",e.toString()));}catch(Exception ignored){}}
    }
    @Override protected void onResume(){super.onResume();ProbeState.visible=true;handler.post(refresh);}
    @Override protected void onPause(){ProbeState.visible=false;handler.removeCallbacks(refresh);super.onPause();}
}
