package com.colin.clipbridge;
import android.content.Context;
import androidx.annotation.NonNull;
import androidx.work.*;
import java.util.concurrent.TimeUnit;
import org.json.JSONObject;

public class RegistrationWorker extends Worker {
    public RegistrationWorker(Context c,WorkerParameters p){super(c,p);}
    static void schedule(Context c){
        enqueue(c,ExistingWorkPolicy.KEEP);
    }
    static void retry(Context c){
        enqueue(c,ExistingWorkPolicy.REPLACE);
    }
    private static void enqueue(Context c,ExistingWorkPolicy policy){
        WorkManager.getInstance(c).enqueueUniqueWork("register-device",policy,new OneTimeWorkRequest.Builder(RegistrationWorker.class).setConstraints(new Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()).setBackoffCriteria(BackoffPolicy.EXPONENTIAL,30,TimeUnit.SECONDS).build());
    }
    @NonNull @Override public Result doWork(){
        try {
            if(Config.prefs(getApplicationContext()).contains("pending"))return Result.success();
            if(!SyncState.ready(getApplicationContext()))Config.status(getApplicationContext(),R.string.sync_restoring);
            Context context=getApplicationContext();JSONObject config=Config.read(context);
            if(!config.has("vapid")){
                JSONObject push=Network.pushConfiguration(config);
                config.put("vapid",push.getString("vapid")).put("computerId",push.getString("computerId")).remove("firebase");
                Config.updateEndpoints(context,config);
            }
            JSONObject subscription=PushRegistration.ensure(context,config);
            if(isStopped())return Result.success();
            JSONObject value=SyncState.device(context,subscription);
            Network.register(config,value);
            SyncState.registered(context,value);
            Config.status(getApplicationContext(),R.string.status_paired);return Result.success();
        }catch(Exception e){
            if(isStopped())return Result.success();
            // A failed refresh does not revoke a session the computer already acknowledged.
            if(SyncState.ready(getApplicationContext())){
                android.util.Log.w("ClipBridgeRegistration","Refresh failed; retaining the acknowledged session ("+e.getClass().getSimpleName()+")");
                Config.status(getApplicationContext(),R.string.status_paired);return Result.success();
            }
            Config.status(getApplicationContext(),R.string.registration_failed,e.getMessage()==null?e.getClass().getSimpleName():e.getMessage());return Result.retry();
        }
    }
}
