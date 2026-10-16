package com.colin.clipbridge;

import android.content.Context;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import org.json.JSONObject;
import org.unifiedpush.android.connector.UnifiedPush;

final class PushRegistration {
    private static CompletableFuture<JSONObject> waiting;
    private static String requested="";
    static void refresh(Context context){
        Config.prefs(context).edit().remove("subscription").remove("subscriptionIv").apply();
        RegistrationWorker.schedule(context);
    }
    static JSONObject ensure(Context context,JSONObject configuration)throws Exception {
        String vapid=configuration.getString("vapid");CompletableFuture<JSONObject> future;
        synchronized(PushRegistration.class){
            try{JSONObject cached=Config.subscription(context);if(vapid.equals(cached.getString("vapid")))return cached.getJSONObject("subscription");}catch(Exception ignored){}
            if(waiting==null||waiting.isDone()||!requested.equals(vapid)){
                requested=vapid;waiting=new CompletableFuture<>();
                Config.prefs(context).edit().putString("requestedVapid",vapid).commit();
                UnifiedPush.saveDistributor(context,context.getPackageName());
                UnifiedPush.register(context,"clipboard","Clipboard Bridge",vapid);
            }
            future=waiting;
        }
        return future.get(25,TimeUnit.SECONDS);
    }
    static synchronized void completed(Context context,JSONObject subscription)throws Exception {
        String vapid=Config.prefs(context).getString("requestedVapid","");
        if(vapid.isEmpty())throw new IllegalStateException("Missing VAPID registration");
        Config.saveSubscription(context,new JSONObject().put("vapid",vapid).put("subscription",subscription));
        if(waiting!=null)waiting.complete(subscription);
        if(Config.prefs(context).contains("config")&&!Config.prefs(context).contains("pending"))RegistrationWorker.schedule(context);
    }
    static synchronized void failed(String reason){if(waiting!=null)waiting.completeExceptionally(new IllegalStateException(reason));}
}
