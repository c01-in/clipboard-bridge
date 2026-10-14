package com.colin.clipbridge;
import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;
import androidx.work.*;
import org.json.JSONObject;

public class MessagingService extends FirebaseMessagingService {
    @Override public void onNewToken(String token){RegistrationWorker.schedule(this);}
    @Override public void onMessageReceived(RemoteMessage message){
        String box=message.getData().get("box");if(box==null||!"1".equals(message.getData().get("v")))return;
        try {
            JSONObject manifest=new JSONObject(new String(Crypto.open(box,Config.read(this).getString("key")),java.nio.charset.StandardCharsets.UTF_8));
            if(manifest.getLong("expiresAt")<=System.currentTimeMillis())return;
            synchronized(MessagingService.class){
                long sequence=manifest.getLong("sequence");
                if(sequence<=Config.prefs(this).getLong("latestScheduledSequence",0))return;
                Config.prefs(this).edit().putLong("latestScheduledSequence",sequence).commit();
            }
            Notices.show(this,"正在接收电脑剪贴板…");
            OneTimeWorkRequest request=new OneTimeWorkRequest.Builder(ReceiveWorker.class).setInputData(new Data.Builder().putString("box",box).build()).setExpedited(OutOfQuotaPolicy.RUN_AS_NON_EXPEDITED_WORK_REQUEST).build();
            WorkManager.getInstance(this).enqueueUniqueWork("receive-clipboard",ExistingWorkPolicy.REPLACE,request);
        }catch(Exception e){Notices.show(this,"接收校验失败，请检查配对配置");}
    }
}
