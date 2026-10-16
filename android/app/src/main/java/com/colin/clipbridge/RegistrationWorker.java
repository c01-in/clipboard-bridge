package com.colin.clipbridge;
import android.content.Context;
import androidx.annotation.NonNull;
import androidx.work.*;
import java.util.concurrent.TimeUnit;
import org.json.JSONObject;

public class RegistrationWorker extends Worker {
    public RegistrationWorker(Context c,WorkerParameters p){super(c,p);}
    static void schedule(Context c){
        WorkManager.getInstance(c).enqueueUniqueWork("register-device",ExistingWorkPolicy.REPLACE,new OneTimeWorkRequest.Builder(RegistrationWorker.class).setConstraints(new Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()).setBackoffCriteria(BackoffPolicy.EXPONENTIAL,30,TimeUnit.SECONDS).build());
    }
    @NonNull @Override public Result doWork(){
        try {
            if(Config.prefs(getApplicationContext()).contains("pending"))return Result.success();
            Config.status(getApplicationContext(),R.string.sync_restoring);
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
        }catch(Exception e){Config.status(getApplicationContext(),R.string.registration_failed,e.getMessage());return Result.retry();}
    }
}
