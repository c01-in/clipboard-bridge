package com.colin.clipbridge;

import android.content.Context;
import androidx.work.WorkManager;
import java.util.UUID;
import org.json.JSONObject;

/** Pause and clipboard writes share one lock; changing generation invalidates already queued messages. */
final class SyncState {
    static final Object LOCK=new Object();
    static boolean paused(Context c){return Config.prefs(c).getBoolean("paused",false);}
    static String generation(Context c){
        synchronized(LOCK){
            String value=Config.prefs(c).getString("receiveGeneration","");
            if(value.isEmpty()){
                value=UUID.randomUUID().toString().replace("-","");
                if(!Config.prefs(c).edit().putString("receiveGeneration",value).putLong("syncRevision",System.currentTimeMillis()*1000).commit())throw new IllegalStateException(c.getString(R.string.error_save_config));
            }
            return value;
        }
    }
    static boolean ready(Context c){synchronized(LOCK){return !paused(c)&&generation(c).equals(Config.prefs(c).getString("registeredGeneration",""));}}
    static boolean accepts(Context c,String generation){synchronized(LOCK){return !paused(c)&&generation(c).equals(generation);}}
    static JSONObject device(Context c,JSONObject subscription)throws Exception {
        synchronized(LOCK){return new JSONObject().put("subscription",subscription).put("deviceName",android.os.Build.MANUFACTURER+" "+android.os.Build.MODEL)
            .put("receiveGeneration",generation(c)).put("revision",Config.prefs(c).getLong("syncRevision",0)).put("paused",paused(c));}
    }
    static void registered(Context c,JSONObject device)throws Exception {
        synchronized(LOCK){
            if(generation(c).equals(device.getString("receiveGeneration")) && Config.prefs(c).getLong("syncRevision",0)==device.getLong("revision")){
                if(!Config.prefs(c).edit().putString("registeredGeneration",generation(c)).commit())throw new Exception(c.getString(R.string.error_save_config));
            }
        }
    }
    static void setPaused(Context c,boolean paused)throws Exception {
        synchronized(LOCK){
            if(paused(c)==paused)return;
            long revision=Math.max(System.currentTimeMillis()*1000,Config.prefs(c).getLong("syncRevision",0)+1);
            if(!Config.prefs(c).edit().putBoolean("paused",paused).putString("receiveGeneration",UUID.randomUUID().toString().replace("-",""))
                .putLong("syncRevision",revision).remove("registeredGeneration").remove("latestScheduledSequence").commit())throw new Exception(c.getString(R.string.error_save_config));
            WorkManager.getInstance(c).cancelUniqueWork("receive-clipboard");
            c.getSystemService(android.app.NotificationManager.class).cancel(1);
        }
        if(Config.prefs(c).contains("config"))RegistrationWorker.schedule(c);
    }
}
