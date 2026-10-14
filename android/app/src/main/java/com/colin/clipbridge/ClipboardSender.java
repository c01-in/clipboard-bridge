package com.colin.clipbridge;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.net.Uri;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.UUID;
import org.json.JSONObject;

final class ClipboardSender {
    private static final int MAX_BYTES=16*1024*1024;
    static final class Input {
        final String text;
        final Uri image;
        Input(String text,Uri image){this.text=text;this.image=image;}
    }

    // Clipboard access happens only in the focused Activity, after the user's tap.
    static Input capture(Context context)throws Exception {
        ClipData clip=context.getSystemService(ClipboardManager.class).getPrimaryClip();
        if(clip==null||clip.getItemCount()==0)throw new Exception("剪贴板为空，请先复制文字或一张图片");
        if(clip.getItemCount()!=1)throw new Exception("一次只能发送一条文字或一张图片");
        ClipData.Item item=clip.getItemAt(0);
        if(item.getUri()!=null){
            Uri uri=item.getUri();
            String type=context.getContentResolver().getType(uri);
            if("content".equals(uri.getScheme())&&((type!=null&&type.startsWith("image/"))||clip.getDescription().hasMimeType("image/*")))
                return new Input(null,uri);
        }
        if(item.getText()!=null&&item.getText().length()>0)return new Input(item.getText().toString(),null);
        throw new Exception("当前剪贴板不含可读取的文字或图片；请在原 App 中使用复制图片");
    }

    static void send(Context context,Input input)throws Exception {
        JSONObject config=Config.read(context);
        byte[] bytes=input.image==null?input.text.getBytes(StandardCharsets.UTF_8):readImage(context,input.image);
        if(bytes.length==0||bytes.length>MAX_BYTES)throw new Exception("内容必须在 1 字节至 16 MiB 之间");
        String id=UUID.randomUUID().toString().replace("-","");
        long sequence=nextSequence(context);
        StringBuilder hash=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(bytes))hash.append(String.format("%02x",b&255));
        JSONObject manifest=new JSONObject().put("id",id).put("sequence",sequence).put("mime",input.image==null?"text/plain":"image/png")
            .put("expiresAt",System.currentTimeMillis()+120000).put("size",bytes.length).put("sha256",hash.toString());
        String key=config.getString("key");
        String body=new JSONObject().put("envelope",Crypto.seal(manifest.toString().getBytes(StandardCharsets.UTF_8),key)).put("data",Crypto.seal(bytes,key)).toString();
        JSONObject response=new JSONObject(Network.request(config,"/api/clipboard",body,16384,30000));
        JSONObject receipt=new JSONObject(new String(Crypto.open(response.getString("data"),key),StandardCharsets.UTF_8));
        if(!id.equals(receipt.getString("id"))||sequence!=receipt.getLong("sequence"))throw new Exception("Windows 确认信息不匹配，未确认写入");
        if(!"applied".equals(receipt.getString("status")))throw new Exception(receipt.optString("detail","Windows 未确认写入，请重试"));
    }

    private static synchronized long nextSequence(Context context)throws Exception {
        long sequence=Math.max(System.currentTimeMillis()*1000,Config.prefs(context).getLong("outgoingSequence",0)+1);
        if(!Config.prefs(context).edit().putLong("outgoingSequence",sequence).commit())throw new Exception("无法保存发送序号");
        return sequence;
    }

    private static byte[] readImage(Context context,Uri uri)throws Exception {
        byte[] source;
        try(InputStream in=context.getContentResolver().openInputStream(uri);LimitedOutput out=new LimitedOutput()){
            if(in==null)throw new Exception("无法读取剪贴板图片，请重新复制");
            byte[] buffer=new byte[8192];int count;while((count=in.read(buffer))!=-1)out.write(buffer,0,count);
            source=out.toByteArray();
        }
        // Preserve PNG bytes and transparency; Windows also validates the actual image.
        if(source.length>=8&&source[0]==(byte)137&&source[1]==80&&source[2]==78&&source[3]==71&&source[4]==13&&source[5]==10&&source[6]==26&&source[7]==10)return source;
        Bitmap bitmap=BitmapFactory.decodeByteArray(source,0,source.length);
        if(bitmap==null)throw new Exception("无法解码剪贴板图片");
        try(LimitedOutput out=new LimitedOutput()){
            if(!bitmap.compress(Bitmap.CompressFormat.PNG,100,out))throw new Exception("图片转换失败或转换后超过 16 MiB");
            return out.toByteArray();
        }finally{bitmap.recycle();}
    }

    private static final class LimitedOutput extends ByteArrayOutputStream {
        @Override public synchronized void write(byte[] bytes,int offset,int length){check(length);super.write(bytes,offset,length);}
        @Override public synchronized void write(int value){check(1);super.write(value);}
        private void check(int length){if(length<0||count>MAX_BYTES-length)throw new IllegalArgumentException("图片原文件或转换结果超过 16 MiB");}
    }
}
