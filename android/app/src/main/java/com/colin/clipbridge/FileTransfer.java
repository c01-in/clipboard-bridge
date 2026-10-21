package com.colin.clipbridge;

import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.List;
import java.util.UUID;
import java.util.function.BooleanSupplier;
import org.json.JSONArray;
import org.json.JSONObject;

/** Versioned, authenticated, bounded-memory file transport, independent of clipboard messages. */
final class FileTransfer {
    static final int CHUNK=256*1024,MAX_FILES=1000;
    interface Progress {void update(int index,int count,long bytes,long total,String name,boolean saving);}
    static String id(){return UUID.randomUUID().toString().replace("-","");}
    static JSONObject request(JSONObject config,String generation,String operation,String id,JSONObject extra)throws Exception{
        String nonce=id();
        JSONObject value=extra==null?new JSONObject():extra;
        value.put("op",operation).put("id",id).put("nonce",nonce).put("expiresAt",System.currentTimeMillis()+120000).put("receiveGeneration",generation);
        JSONObject box=new JSONObject(Network.request(config,"/api/files",Network.encryptedBody(config,value),1024*1024,30000));
        JSONObject result=new JSONObject(new String(Crypto.open(box.getString("data"),config.getString("key")),StandardCharsets.UTF_8));
        if(!id.equals(result.optString("id"))||!nonce.equals(result.optString("nonce")))throw new Exception(LocalText.get(R.string.error_receipt));
        if("error".equals(result.optString("status")))throw new Exception(result.optString("detail"));
        return result;
    }
    static JSONObject retry(JSONObject config,String generation,String operation,String id,JSONObject extra,BooleanSupplier cancelled)throws Exception{
        Exception failure=null;
        for(int attempt=0;attempt<3;attempt++){
            if(cancelled.getAsBoolean())throw new InterruptedException(LocalText.get(R.string.files_cancelled));
            try{return request(config,generation,operation,id,extra==null?null:new JSONObject(extra.toString()));}
            catch(Exception e){failure=e;if(attempt<2)Thread.sleep(400);}
        }
        throw failure;
    }
    static JSONObject describe(Context context,Uri uri)throws Exception{
        if(uri==null||!"content".equals(uri.getScheme()))throw new Exception(LocalText.get(R.string.error_share_content));
        String name=null;long size=-1;
        try(Cursor cursor=context.getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE},null,null,null)){
            if(cursor!=null&&cursor.moveToFirst()){
                int n=cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME),s=cursor.getColumnIndex(OpenableColumns.SIZE);
                if(n>=0&&!cursor.isNull(n))name=cursor.getString(n);
                if(s>=0&&!cursor.isNull(s))size=cursor.getLong(s);
            }
        }
        if(name==null||name.trim().isEmpty())name="file";
        if(name.length()>240)throw new Exception(LocalText.get(R.string.files_name_long));
        String mime=context.getContentResolver().getType(uri);
        return new JSONObject().put("name",name).put("mime",mime==null?"application/octet-stream":mime).put("size",Math.max(-1,size));
    }
    static void send(Context context,List<Uri> uris,BooleanSupplier cancelled,Progress progress)throws Exception{
        if(uris.isEmpty()||uris.size()>MAX_FILES)throw new Exception(LocalText.get(R.string.files_selection_limit));
        if(!SyncState.ready(context))throw new Exception(LocalText.get(R.string.sync_paused));
        JSONObject config=Config.read(context);String generation=SyncState.generation(context),id=id();
        BooleanSupplier stopped=()->cancelled.getAsBoolean()||!SyncState.accepts(context,generation);
        JSONArray files=new JSONArray();long total=0;boolean unknown=false;
        for(Uri uri:uris){JSONObject file=describe(context,uri);files.put(file);if(file.getLong("size")<0)unknown=true;else total=Math.addExact(total,file.getLong("size"));}
        if(unknown)total=-1;
        long sequence;
        synchronized(SyncState.LOCK){sequence=Math.max(System.currentTimeMillis()*1000,Config.prefs(context).getLong("fileOutgoingSequence",0)+1);
            if(!Config.prefs(context).edit().putLong("fileOutgoingSequence",sequence).commit())throw new Exception(LocalText.get(R.string.error_save_sequence));}
        long expiry=System.currentTimeMillis()+3600000;
        JSONObject batch=new JSONObject().put("id",id).put("sequence",sequence).put("expiresAt",expiry).put("receiveGeneration",generation).put("files",files);
        if(batch.toString().getBytes(StandardCharsets.UTF_8).length>512*1024)throw new Exception(LocalText.get(R.string.files_selection_limit));
        boolean saved=false;
        try{
            retry(config,generation,"start",id,new JSONObject().put("batch",batch),stopped);
            long done=0;
            byte[] buffer=new byte[CHUNK];
            for(int i=0;i<uris.size();i++){
                long offset=0;MessageDigest hash=MessageDigest.getInstance("SHA-256");String name=files.getJSONObject(i).getString("name");
                progress.update(i,uris.size(),done,total,name,false);
                try(InputStream input=context.getContentResolver().openInputStream(uris.get(i))){
                    if(input==null)throw new Exception(LocalText.get(R.string.files_read_failed,name));
                    int n;
                    while((n=readChunk(input,buffer))>0){
                        if(stopped.getAsBoolean()||System.currentTimeMillis()>=expiry)throw new InterruptedException(LocalText.get(R.string.files_cancelled));
                        byte[] bytes=n==buffer.length?buffer:java.util.Arrays.copyOf(buffer,n);
                        JSONObject response=retry(config,generation,"write",id,new JSONObject().put("index",i).put("offset",offset).put("data",java.util.Base64.getEncoder().encodeToString(bytes)),stopped);
                        if(!"chunk".equals(response.getString("status"))||response.getLong("offset")!=offset+n)throw new Exception(LocalText.get(R.string.error_receipt));
                        hash.update(buffer,0,n);offset+=n;done+=n;progress.update(i,uris.size(),done,total,name,false);
                    }
                }
                retry(config,generation,"finish",id,new JSONObject().put("index",i).put("size",offset).put("sha256",hex(hash.digest())),stopped);
            }
            retry(config,generation,"commit",id,null,stopped);
            progress.update(uris.size(),uris.size(),done,total,"",true);
            while(System.currentTimeMillis()<expiry){
                JSONObject response=retry(config,generation,"result",id,null,stopped);
                if("saved".equals(response.getString("status"))){saved=true;return;}
                if(!"saving".equals(response.getString("status")))throw new Exception(LocalText.get(R.string.error_receipt));
                Thread.sleep(500);
            }
            throw new Exception(LocalText.get(R.string.files_interrupted));
        }finally{
            if(!saved)try{request(config,generation,"abort",id,null);}catch(Exception ignored){}
        }
    }
    static int readChunk(InputStream input,byte[] buffer)throws Exception{
        int count=0,n;while(count<buffer.length&&(n=input.read(buffer,count,buffer.length-count))!=-1){if(n==0){int one=input.read();if(one<0)break;buffer[count++]=(byte)one;}else count+=n;}return count;
    }
    static String hex(byte[] bytes){StringBuilder value=new StringBuilder();for(byte b:bytes)value.append(String.format(java.util.Locale.ROOT,"%02x",b&255));return value.toString();}
}
