package com.colin.clipbridge;

import android.content.Context;
import androidx.work.*;
import org.json.JSONObject;
import org.unifiedpush.android.connector.FailedReason;
import org.unifiedpush.android.connector.MessagingReceiver;
import org.unifiedpush.android.connector.data.PushEndpoint;
import org.unifiedpush.android.connector.data.PushMessage;

@SuppressWarnings("deprecation")
public final class PushReceiver extends MessagingReceiver {
    @Override public void onNewEndpoint(Context context,PushEndpoint endpoint,String instance){
        try{
            if(endpoint.getPubKeySet()==null)throw new IllegalStateException("Missing Web Push keys");
            PushRegistration.completed(context,new JSONObject().put("endpoint",endpoint.getUrl()).put("keys",new JSONObject()
                .put("p256dh",endpoint.getPubKeySet().getPubKey()).put("auth",endpoint.getPubKeySet().getAuth())));
        }catch(Exception e){PushRegistration.failed(e.getMessage());Config.status(context,R.string.registration_failed,e.getMessage());}
    }
    @Override public void onRegistrationFailed(Context context,FailedReason reason,String instance){PushRegistration.failed(reason.toString());Config.status(context,R.string.registration_failed,reason.toString());}
    @Override public void onUnregistered(Context context,String instance){Config.prefs(context).edit().remove("subscription").remove("subscriptionIv").apply();RegistrationWorker.schedule(context);}
    @Override public void onMessage(Context context,PushMessage message,String instance){
        if(!message.getDecrypted())return;
        try{
            JSONObject payload=new JSONObject(new String(message.getContent(),java.nio.charset.StandardCharsets.UTF_8));
            if(!"1".equals(payload.optString("v")))return;String box=payload.getString("box");
            JSONObject manifest=new JSONObject(new String(Crypto.open(box,Config.read(context).getString("key")),java.nio.charset.StandardCharsets.UTF_8));
            if(manifest.getLong("expiresAt")<=System.currentTimeMillis())return;
            synchronized(SyncState.LOCK){
                if(!SyncState.accepts(context,manifest.optString("receiveGeneration")))return;
                if("application/vnd.clipboardbridge.files".equals(manifest.optString("mime"))){
                    String id=manifest.getString("id");if(!id.matches("[0-9a-f]{32}"))return;
                    OneTimeWorkRequest files=new OneTimeWorkRequest.Builder(FileReceiveWorker.class).setInputData(new Data.Builder().putString("box",box).build())
                        .setExpedited(OutOfQuotaPolicy.RUN_AS_NON_EXPEDITED_WORK_REQUEST).setBackoffCriteria(BackoffPolicy.LINEAR,10,java.util.concurrent.TimeUnit.SECONDS).addTag("receive-files").build();
                    WorkManager.getInstance(context).enqueueUniqueWork("receive-files-"+id,ExistingWorkPolicy.KEEP,files);return;
                }
                long sequence=manifest.getLong("sequence");if(sequence<=Config.prefs(context).getLong("latestScheduledSequence",0))return;
                if(!Config.prefs(context).edit().putLong("latestScheduledSequence",sequence).commit())return;
                Notices.show(context,R.string.receiving);
                OneTimeWorkRequest request=new OneTimeWorkRequest.Builder(ReceiveWorker.class).setInputData(new Data.Builder().putString("box",box).build())
                    .setExpedited(OutOfQuotaPolicy.RUN_AS_NON_EXPEDITED_WORK_REQUEST).build();
                WorkManager.getInstance(context).enqueueUniqueWork("receive-clipboard",ExistingWorkPolicy.REPLACE,request);
            }
        }catch(Exception e){if(!SyncState.paused(context))Notices.show(context,R.string.receive_verify_failed);}
    }
}
