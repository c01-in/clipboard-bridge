package com.colin.clipbridge.webpushprobe;

import android.app.Notification;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;
import org.unifiedpush.android.connector.FailedReason;
import org.unifiedpush.android.connector.MessagingReceiver;
import org.unifiedpush.android.connector.data.PushEndpoint;
import org.unifiedpush.android.connector.data.PushMessage;

@SuppressWarnings("deprecation")
public final class ProbeReceiver extends MessagingReceiver {
    @Override public void onNewEndpoint(Context context, PushEndpoint endpoint, String instance) {
        try {
            if(endpoint.getPubKeySet()==null)throw new IllegalStateException("No Web Push encryption key");
            JSONObject subscription=new JSONObject().put("endpoint",endpoint.getUrl())
                .put("keys",new JSONObject().put("p256dh",endpoint.getPubKeySet().getPubKey()).put("auth",endpoint.getPubKeySet().getAuth()));
            ProbeState.write(context,"subscription.json",subscription);
            ProbeState.event(context,"registered",new JSONObject().put("host",android.net.Uri.parse(endpoint.getUrl()).getHost()).put("encrypted",true));
        } catch(Exception e){fail(context,e);}
    }
    @Override public void onMessage(Context context, PushMessage message, String instance) {
        try {
            if(!message.getDecrypted())throw new SecurityException("Push did not decrypt; rejected");
            JSONObject payload=new JSONObject(new String(message.getContent(),StandardCharsets.UTF_8));
            String id=payload.getString("id");
            if(!id.matches("[0-9a-f]{32}"))throw new SecurityException("Invalid probe id");
            long sentAt=payload.getLong("sentAt");
            if(Math.abs(System.currentTimeMillis()-sentAt)>600000)throw new SecurityException("Expired probe");
            String text=payload.getString("text");
            if(text.length()>200)throw new SecurityException("Oversized probe");
            ProbeState.event(context,"message",new JSONObject().put("id",id).put("sentAt",sentAt)
                .put("decrypted",true).put("text",text).put("clockDeltaMs",System.currentTimeMillis()-sentAt));
            PendingIntent open=PendingIntent.getActivity(context,0,new Intent(context,MainActivity.class),PendingIntent.FLAG_IMMUTABLE);
            Notification notification=new Notification.Builder(context,"probe").setSmallIcon(R.drawable.probe_icon)
                .setContentTitle("Web Push 实际收到").setContentText(text).setContentIntent(open).setAutoCancel(true).build();
            context.getSystemService(NotificationManager.class).notify(id.hashCode(),notification);
        }catch(Exception e){fail(context,e);}
    }
    @Override public void onRegistrationFailed(Context context, FailedReason reason, String instance){
        try{ProbeState.event(context,"registration-failed",new JSONObject().put("reason",reason.toString()));}catch(Exception ignored){}
    }
    @Override public void onUnregistered(Context context,String instance){ProbeState.event(context,"unregistered",new JSONObject());}
    private void fail(Context context,Exception error){
        try{ProbeState.event(context,"rejected",new JSONObject().put("error",error.toString()));}catch(Exception ignored){}
    }
}
