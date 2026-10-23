package com.colin.clipbridge;
import android.content.*;
import android.net.Uri;
import android.graphics.BitmapFactory;
import androidx.annotation.NonNull;
import androidx.work.*;
import com.google.common.util.concurrent.ListenableFuture;
import androidx.concurrent.futures.CallbackToFutureAdapter;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.MessageDigest;
import java.util.concurrent.TimeUnit;
import org.json.JSONObject;

public class ReceiveWorker extends Worker {
    public ReceiveWorker(Context c,WorkerParameters p){super(c,p);}
    @NonNull @Override public ListenableFuture<ForegroundInfo> getForegroundInfoAsync(){return CallbackToFutureAdapter.getFuture(completer->{completer.set(new ForegroundInfo(1,Notices.create(getApplicationContext(),getApplicationContext().getString(R.string.receiving))));return "clipboard-foreground-info";});}
    @NonNull @Override public Result doWork(){
        Context c=getApplicationContext();JSONObject manifest=null,config=null;
        try {
            config=Config.read(c);manifest=new JSONObject(new String(Crypto.open(getInputData().getString("box"),config.getString("key")),StandardCharsets.UTF_8));
            long seq=manifest.getLong("sequence");String id=manifest.getString("id"),mime=manifest.getString("mime");
            if(!SyncState.accepts(c,manifest.optString("receiveGeneration"))){ack(c,manifest,"discarded",null);return Result.success();}
            if(!id.matches("[0-9a-f]{32}")||(!mime.equals("text/plain")&&!mime.equals("image/png")))throw new Exception(LocalText.get(R.string.error_content_format));
            if(manifest.getLong("expiresAt")<=System.currentTimeMillis())return Result.success();
            synchronized(SyncState.LOCK){if(seq<=Config.prefs(c).getLong("lastSequence",0)){ack(c,manifest,"applied",null);return Result.success();}}
            byte[] bytes;
            if(!manifest.isNull("inline")) bytes=android.util.Base64.decode(manifest.getString("inline"),android.util.Base64.NO_WRAP);
            else {
                if(!id.equals(manifest.getString("blob")))throw new Exception(LocalText.get(R.string.error_image_id));
                String encrypted=Network.request(config,"/blob/"+id,null,24*1024*1024);
                bytes=Crypto.open(encrypted,config.getString("key"));
            }
            if(bytes.length!=manifest.getInt("size")||bytes.length>16*1024*1024)throw new Exception(LocalText.get(R.string.error_receive_size));
            StringBuilder hash=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(bytes))hash.append(String.format("%02x",b&255));
            if(!hash.toString().equals(manifest.getString("sha256")))throw new Exception(LocalText.get(R.string.error_integrity));
            if(isStopped()||manifest.getLong("expiresAt")<=System.currentTimeMillis())return Result.success();
            synchronized(SyncState.LOCK){
                if(!SyncState.accepts(c,manifest.optString("receiveGeneration"))){ack(c,manifest,"discarded",null);return Result.success();}
                if(seq<=Config.prefs(c).getLong("lastSequence",0))return Result.success();
                ClipboardManager clipboard=c.getSystemService(ClipboardManager.class);
                if(mime.equals("text/plain"))clipboard.setPrimaryClip(ClipData.newPlainText(LocalText.get(R.string.clipboard_from_windows),new String(bytes,StandardCharsets.UTF_8)));
                else {
                    BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;BitmapFactory.decodeByteArray(bytes,0,bytes.length,bounds);
                    if(bounds.outWidth<=0||bounds.outHeight<=0)throw new Exception(LocalText.get(R.string.error_decode_image));
                    File dir=new File(c.getFilesDir(),"clips");dir.mkdirs();File image=new File(dir,id+".png");Files.write(image.toPath(),bytes);
                    clipboard.setPrimaryClip(ClipData.newUri(c.getContentResolver(),LocalText.get(R.string.clipboard_image_windows),Uri.parse("content://"+c.getPackageName()+".images/"+id+".png")));
                    File[] old=dir.listFiles();if(old!=null)for(File f:old)if(!f.equals(image)&&System.currentTimeMillis()-f.lastModified()>24*3600*1000L)f.delete();
                }
                if(!Config.prefs(c).edit().putLong("lastSequence",seq).putString("lastId",id).putString("lastMime",mime).commit())throw new Exception(LocalText.get(R.string.error_save_receipt));
            }
            if(!SyncState.paused(c))Notices.received(c,mime.equals("image/png")?R.string.received_image:R.string.received_text,id);
            ack(c,manifest,"applied",null);return Result.success();
        }catch(Exception e){
            if(isStopped()||SyncState.paused(c))return Result.failure();
            Notices.show(c,R.string.receive_failed,e.getMessage());
            if(manifest!=null&&getRunAttemptCount()<3&&manifest.optLong("expiresAt")>System.currentTimeMillis())return Result.retry();
            if(manifest!=null)ack(c,manifest,"error",e.getMessage());return Result.failure();
        }
    }
    private static void ack(Context c,JSONObject manifest,String status,String detail){
        try {
            String data=new JSONObject().put("id",manifest.getString("id")).put("sequence",manifest.getLong("sequence")).put("status",status).put("detail",detail==null?JSONObject.NULL:detail).toString();
            WorkManager.getInstance(c).enqueue(new OneTimeWorkRequest.Builder(AckWorker.class).setInputData(new Data.Builder().putString("receipt",data).build()).setConstraints(new Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()).setBackoffCriteria(BackoffPolicy.EXPONENTIAL,30,TimeUnit.SECONDS).build());
        }catch(Exception ignored){}
    }
}
