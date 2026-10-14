package com.colin.clipbridge;
import android.content.Context;
import androidx.annotation.NonNull;
import androidx.work.*;
import org.json.JSONObject;
public class AckWorker extends Worker {
    public AckWorker(Context c,WorkerParameters p){super(c,p);}
    @NonNull @Override public Result doWork(){try{JSONObject config=Config.read(getApplicationContext());Network.request(config,"/api/ack",Network.encryptedBody(config,new JSONObject(getInputData().getString("receipt"))),1024);return Result.success();}catch(Exception e){return getRunAttemptCount()<8?Result.retry():Result.failure();}}
}
