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
    private static final Object CLIP_LOCK=new Object();
    public ReceiveWorker(Context c,WorkerParameters p){super(c,p);}
    @NonNull @Override public ListenableFuture<ForegroundInfo> getForegroundInfoAsync(){return CallbackToFutureAdapter.getFuture(completer->{completer.set(new ForegroundInfo(1,Notices.create(getApplicationContext(),"正在接收电脑剪贴板…")));return "clipboard-foreground-info";});}
    @NonNull @Override public Result doWork(){
        Context c=getApplicationContext();JSONObject manifest=null,config=null;
        try {
            config=Config.read(c);manifest=new JSONObject(new String(Crypto.open(getInputData().getString("box"),config.getString("key")),StandardCharsets.UTF_8));
            long seq=manifest.getLong("sequence");String id=manifest.getString("id"),mime=manifest.getString("mime");
            if(!id.matches("[0-9a-f]{32}")||(!mime.equals("text/plain")&&!mime.equals("image/png")))throw new Exception("内容格式无效");
            if(manifest.getLong("expiresAt")<=System.currentTimeMillis())return Result.success();
            synchronized(CLIP_LOCK){if(seq<=Config.prefs(c).getLong("lastSequence",0)){ack(c,manifest,"applied",null);return Result.success();}}
            byte[] bytes;
            if(!manifest.isNull("inline")) bytes=android.util.Base64.decode(manifest.getString("inline"),android.util.Base64.NO_WRAP);
            else {
                if(!id.equals(manifest.getString("blob")))throw new Exception("图片标识无效");
                String encrypted=Network.request(config,"/blob/"+id,null,24*1024*1024);
                bytes=Crypto.open(encrypted,config.getString("key"));
            }
            if(bytes.length!=manifest.getInt("size")||bytes.length>16*1024*1024)throw new Exception("接收大小不匹配");
            StringBuilder hash=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(bytes))hash.append(String.format("%02x",b&255));
            if(!hash.toString().equals(manifest.getString("sha256")))throw new Exception("内容完整性校验失败");
            if(isStopped()||manifest.getLong("expiresAt")<=System.currentTimeMillis())return Result.success();
            synchronized(CLIP_LOCK){
                if(seq<=Config.prefs(c).getLong("lastSequence",0))return Result.success();
                ClipboardManager clipboard=c.getSystemService(ClipboardManager.class);
                if(mime.equals("text/plain"))clipboard.setPrimaryClip(ClipData.newPlainText("来自 Windows",new String(bytes,StandardCharsets.UTF_8)));
                else {
                    BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;BitmapFactory.decodeByteArray(bytes,0,bytes.length,bounds);
                    if(bounds.outWidth<=0||bounds.outHeight<=0)throw new Exception("图片无法解码");
                    File dir=new File(c.getFilesDir(),"clips");dir.mkdirs();File image=new File(dir,id+".png");Files.write(image.toPath(),bytes);
                    clipboard.setPrimaryClip(ClipData.newUri(c.getContentResolver(),"来自 Windows 的图片",Uri.parse("content://com.colin.clipbridge.images/"+id+".png")));
                    File[] old=dir.listFiles();if(old!=null)for(File f:old)if(!f.equals(image)&&System.currentTimeMillis()-f.lastModified()>24*3600*1000L)f.delete();
                }
                if(!Config.prefs(c).edit().putLong("lastSequence",seq).putString("lastId",id).putString("lastMime",mime).commit())throw new Exception("无法保存接收记录");
            }
            Notices.show(c,mime.equals("image/png")?"图片已写入剪贴板，可在支持图片粘贴的 App 中粘贴":"文字已写入剪贴板，可以直接粘贴");
            ack(c,manifest,"applied",null);return Result.success();
        }catch(Exception e){
            if(isStopped())return Result.failure();
            Notices.show(c,"接收失败："+e.getMessage());
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
