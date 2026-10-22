package com.colin.clipbridge;

import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.DocumentsContract;
import android.provider.MediaStore;
import androidx.annotation.NonNull;
import androidx.work.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import org.json.*;

/** Saves files to public Downloads (or a user-selected SAF folder), never to the clipboard. */
public final class FileReceiveWorker extends Worker {
    public FileReceiveWorker(Context context,WorkerParameters parameters){super(context,parameters);}
    private Notification notice(String text,int progress,boolean indeterminate){
        Context c=getApplicationContext();NotificationManager manager=c.getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel("files",c.getString(R.string.files_channel),NotificationManager.IMPORTANCE_LOW));
        PendingIntent open=PendingIntent.getActivity(c,22,new Intent(c,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(c,"files").setSmallIcon(android.R.drawable.stat_sys_download).setContentTitle(c.getString(R.string.app_name))
            .setContentText(text).setProgress(1000,progress,indeterminate).setOngoing(true).setContentIntent(open).setVisibility(Notification.VISIBILITY_PRIVATE).build();
    }
    private ForegroundInfo foreground(String text,int progress,boolean indeterminate){
        Notification notification=notice(text,progress,indeterminate);
        return Build.VERSION.SDK_INT>=29?new ForegroundInfo(22,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC):new ForegroundInfo(22,notification);
    }
    @NonNull @Override public com.google.common.util.concurrent.ListenableFuture<ForegroundInfo> getForegroundInfoAsync(){
        return androidx.concurrent.futures.CallbackToFutureAdapter.getFuture(completer->{completer.set(foreground(getApplicationContext().getString(R.string.receiving),0,true));return "file-receive-foreground";});
    }
    @NonNull @Override public Result doWork(){
        Context context=getApplicationContext();JSONObject config=null,wake=null;File journal=null;JSONObject record=null;
        String id="",generation="";
        FileTransfer.Session session=null;
        try{
            config=Config.read(context);wake=new JSONObject(new String(Crypto.open(getInputData().getString("box"),config.getString("key")),StandardCharsets.UTF_8));
            id=wake.getString("id");generation=wake.getString("receiveGeneration");
            if(!id.matches("[0-9a-f]{32}")||!SyncState.accepts(context,generation))return Result.success();
            setForegroundAsync(foreground(context.getString(R.string.receiving),0,true)).get();
            session=FileTransfer.open(config,generation,id);
            File directory=new File(context.getFilesDir(),"file-receipts");if(!directory.isDirectory()&&!directory.mkdirs())throw new IOException("Cannot create file receipts.");
            journal=new File(directory,id+".json");
            record=journal.isFile()?new JSONObject(new String(Files.readAllBytes(journal.toPath()),StandardCharsets.UTF_8)):new JSONObject().put("files",new JSONArray());
            if(record.optBoolean("complete")&&record.optLong("sequence")==wake.getLong("sequence")&&generation.equals(record.optString("receiveGeneration"))){
                if(!record.optBoolean("acknowledged")){session.request("saved",null);record.put("acknowledged",true);persist(journal,record);}
                return Result.success();
            }
            JSONObject offer=session.request("offer",null),batch=offer.getJSONObject("batch");
            if(!id.equals(batch.getString("id"))||!generation.equals(batch.getString("receiveGeneration"))||batch.getLong("sequence")!=wake.getLong("sequence"))throw new Exception(context.getString(R.string.error_receipt));
            JSONArray files=batch.getJSONArray("files");if(files.length()<1||files.length()>FileTransfer.MAX_FILES)throw new Exception(context.getString(R.string.files_selection_limit));
            JSONArray receipts=record.getJSONArray("files");
            long total=0,done=0;
            for(int i=0;i<files.length();i++){long size=files.getJSONObject(i).getLong("size");if(size<0)throw new IOException("Invalid file size.");total=Math.addExact(total,size);}
            long lastNotice=0;
            for(int i=0;i<files.length();i++){
                JSONObject item=files.getJSONObject(i);long size=item.getLong("size");
                if(i<receipts.length()&&receipts.getJSONObject(i).optBoolean("done")){done+=size;continue;}
                if(i<receipts.length())delete(context,Uri.parse(receipts.getJSONObject(i).getString("uri")));
                Uri output=create(context,item);
                receipts.put(i,new JSONObject().put("uri",output.toString()).put("done",false));persist(journal,record);
                try(OutputStream stream=context.getContentResolver().openOutputStream(output,"w")){
                    if(stream==null)throw new IOException(context.getString(R.string.files_save_failed,item.getString("name")));
                    long offset=0;
                    while(offset<size){
                        if(isStopped()||!SyncState.accepts(context,generation)||session.expired())throw new InterruptedException(context.getString(R.string.files_cancelled));
                        final String acceptedGeneration=generation;
                        JSONObject chunk=session.retry("read",new JSONObject().put("index",i).put("offset",offset),()->isStopped()||!SyncState.accepts(context,acceptedGeneration));
                        byte[] bytes=java.util.Base64.getDecoder().decode(chunk.getString("data"));
                        if(!"chunk".equals(chunk.getString("status"))||bytes.length<1||bytes.length>FileTransfer.CHUNK||offset+bytes.length>size||chunk.getLong("offset")!=offset+bytes.length)throw new IOException(context.getString(R.string.error_integrity));
                        stream.write(bytes);offset+=bytes.length;done+=bytes.length;
                        if(android.os.SystemClock.elapsedRealtime()-lastNotice>=500){
                            lastNotice=android.os.SystemClock.elapsedRealtime();
                            context.getSystemService(NotificationManager.class).notify(22,notice(context.getString(R.string.files_count,String.valueOf(i+1),String.valueOf(files.length()))+" · "+item.getString("name"),total==0?0:(int)((double)done/total*1000),false));
                        }
                    }
                    stream.flush();
                }
                if(Build.VERSION.SDK_INT>=29&&"media".equals(output.getAuthority())){
                    ContentValues values=new ContentValues();values.put(MediaStore.MediaColumns.IS_PENDING,0);
                    if(context.getContentResolver().update(output,values,null,null)!=1)throw new IOException(context.getString(R.string.files_save_failed,item.getString("name")));
                }
                receipts.getJSONObject(i).put("done",true);persist(journal,record);
            }
            final String acceptedGeneration=generation;
            record.put("complete",true).put("sequence",wake.getLong("sequence")).put("receiveGeneration",generation);persist(journal,record);
            session.retry("saved",null,()->isStopped()||!SyncState.accepts(context,acceptedGeneration));
            record.put("acknowledged",true);persist(journal,record);
            Config.status(context,R.string.files_received_phone,String.valueOf(files.length()));
            showResult(context,context.getString(R.string.files_received_phone,String.valueOf(files.length())));
            return Result.success();
        }catch(Exception e){
            if(isStopped()||SyncState.paused(context)){cleanupPartial(context,record);return Result.failure();}
            if(getRunAttemptCount()<2&&wake!=null&&(session==null||!session.expired()))return Result.retry();
            cleanupPartial(context,record);
            if(session!=null)try{session.request("reject",new JSONObject().put("data",String.valueOf(e.getMessage())));}catch(Exception ignored){}
            Config.status(context,R.string.files_save_failed,String.valueOf(e.getMessage()));showResult(context,context.getString(R.string.files_save_failed,String.valueOf(e.getMessage())));
            return Result.failure();
        }
    }
    private static void cleanupPartial(Context context,JSONObject record){
        if(record==null)return;
        JSONArray entries=record.optJSONArray("files");if(entries==null)return;
        for(int i=0;i<entries.length();i++)try{JSONObject file=entries.getJSONObject(i);if(!file.optBoolean("done"))delete(context,Uri.parse(file.getString("uri")));}catch(Exception ignored){}
    }
    static void persist(File journal,JSONObject value)throws Exception{
        File temporary=new File(journal.getPath()+".tmp");
        try(FileOutputStream output=new FileOutputStream(temporary)){output.write(value.toString().getBytes(StandardCharsets.UTF_8));output.getFD().sync();}
        Files.move(temporary.toPath(),journal.toPath(),StandardCopyOption.REPLACE_EXISTING);
    }
    private static Uri create(Context context,JSONObject item)throws Exception{
        String name=item.getString("name").replace('/','_').replace('\\','_').replace('\0','_');
        if(name.isEmpty()||name.length()>240)throw new IOException("Invalid filename.");
        String mime=item.optString("mime","application/octet-stream");
        if("application/octet-stream".equals(mime)){
            int dot=name.lastIndexOf('.');if(dot>=0){String detected=android.webkit.MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot+1).toLowerCase(java.util.Locale.ROOT));if(detected!=null)mime=detected;}
        }
        String selected=Config.prefs(context).getString("receiveFolder","");
        if(!selected.isEmpty()){
            Uri tree=Uri.parse(selected);Uri parent=DocumentsContract.buildDocumentUriUsingTree(tree,DocumentsContract.getTreeDocumentId(tree));
            Uri result=DocumentsContract.createDocument(context.getContentResolver(),parent,mime,name);if(result==null)throw new IOException(context.getString(R.string.files_folder_invalid));return result;
        }
        if(Build.VERSION.SDK_INT<29)throw new IOException(context.getString(R.string.files_choose_receive_folder));
        ContentValues values=new ContentValues();values.put(MediaStore.MediaColumns.DISPLAY_NAME,name);values.put(MediaStore.MediaColumns.MIME_TYPE,mime);
        values.put(MediaStore.MediaColumns.RELATIVE_PATH,Environment.DIRECTORY_DOWNLOADS+"/Clipboard Bridge");values.put(MediaStore.MediaColumns.IS_PENDING,1);
        Uri result=context.getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI,values);if(result==null)throw new IOException(context.getString(R.string.files_folder_invalid));return result;
    }
    private static void delete(Context context,Uri uri){try{if("media".equals(uri.getAuthority()))context.getContentResolver().delete(uri,null,null);else DocumentsContract.deleteDocument(context.getContentResolver(),uri);}catch(Exception ignored){}}
    private static void showResult(Context context,String text){
        if(Build.VERSION.SDK_INT<33||context.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)==android.content.pm.PackageManager.PERMISSION_GRANTED){
            Notification notification=Notices.create(context,text);context.getSystemService(NotificationManager.class).notify(23,notification);
        }
    }
}
