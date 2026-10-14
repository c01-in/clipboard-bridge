package com.colin.clipbridge;
import android.content.Context;
import android.os.Build;
import androidx.annotation.NonNull;
import androidx.work.*;
import com.google.firebase.messaging.FirebaseMessaging;
import com.google.android.gms.tasks.Tasks;
import java.util.concurrent.TimeUnit;
import org.json.JSONObject;

public class RegistrationWorker extends Worker {
    public RegistrationWorker(Context c,WorkerParameters p){super(c,p);}
    static void schedule(Context c){
        WorkManager.getInstance(c).enqueueUniqueWork("register-device",ExistingWorkPolicy.REPLACE,new OneTimeWorkRequest.Builder(RegistrationWorker.class).setConstraints(new Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()).setBackoffCriteria(BackoffPolicy.EXPONENTIAL,30,TimeUnit.SECONDS).build());
    }
    @NonNull @Override public Result doWork(){
        try {
            JSONObject config=Config.read(getApplicationContext());Config.initialize(getApplicationContext());
            String token=Tasks.await(FirebaseMessaging.getInstance().getToken(),20,TimeUnit.SECONDS);
            JSONObject value=new JSONObject().put("token",token).put("deviceName",Build.MANUFACTURER+" "+Build.MODEL);
            Network.request(config,"/api/register",Network.encryptedBody(config,value),1024);
            Config.status(getApplicationContext(),"配对成功，等待 Windows 剪贴板");return Result.success();
        }catch(Exception e){Config.status(getApplicationContext(),"配对尚未完成："+e.getMessage());return Result.retry();}
    }
}
