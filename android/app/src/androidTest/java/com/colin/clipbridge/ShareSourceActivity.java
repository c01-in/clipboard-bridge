package com.colin.clipbridge;

import android.app.Activity;
import android.content.*;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Bundle;
import android.widget.TextView;
import java.io.File;
import java.io.FileOutputStream;

/** A separate-UID source app, packaged only in the instrumentation APK. */
public final class ShareSourceActivity extends Activity {
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);TextView view=new TextView(this);view.setText("Clipboard Bridge share test source");setContentView(view);
        String mode=getIntent().getStringExtra("mode");
        if("copy".equals(mode)){
            getSystemService(ClipboardManager.class).setPrimaryClip(ClipData.newPlainText("test","Independent source copy test"));return;
        }
        Intent share=new Intent(Intent.ACTION_SEND).setPackage("com.colin.clipbridge.debug");
        if("files".equals(mode)){
            try{
                byte[] buffer=new byte[256*1024];new java.security.SecureRandom().nextBytes(buffer);
                try(FileOutputStream out=new FileOutputStream(new File(getCacheDir(),"large.bin"))){for(int i=0;i<73;i++)out.write(buffer);}
                try(FileOutputStream out=new FileOutputStream(new File(getCacheDir(),"empty.dat"))){}
                Bitmap image=Bitmap.createBitmap(8,8,Bitmap.Config.ARGB_8888);image.eraseColor(0xff7044ff);
                try(FileOutputStream out=new FileOutputStream(new File(getCacheDir(),"fixture.png"))){image.compress(Bitmap.CompressFormat.PNG,100,out);}finally{image.recycle();}
            }catch(Exception e){throw new IllegalStateException(e);}
            java.util.ArrayList<Uri> uris=new java.util.ArrayList<>();
            for(String name:new String[]{"large.bin","fixture.png","empty.dat"})uris.add(Uri.parse("content://"+getPackageName()+".fixture/"+name));
            ClipData clip=ClipData.newRawUri("files",uris.get(0));for(int i=1;i<uris.size();i++)clip.addItem(new ClipData.Item(uris.get(i)));
            share.setAction(Intent.ACTION_SEND_MULTIPLE);share.setType("*/*");share.putParcelableArrayListExtra(Intent.EXTRA_STREAM,uris);share.setClipData(clip);share.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            startActivity(share);finish();return;
        }
        if("image".equals(mode)){
            Bitmap bitmap=Bitmap.createBitmap(8,8,Bitmap.Config.ARGB_8888);bitmap.eraseColor(0x807044ff);
            try(FileOutputStream out=new FileOutputStream(new File(getCacheDir(),"fixture.png"))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}
            catch(Exception e){throw new IllegalStateException(e);}finally{bitmap.recycle();}
            Uri uri=Uri.parse("content://"+getPackageName()+".fixture/fixture.png");
            share.setType("image/png");share.putExtra(Intent.EXTRA_STREAM,uri);share.setClipData(ClipData.newRawUri("test image",uri));share.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        }else share.setType("text/plain").putExtra(Intent.EXTRA_TEXT,"External source → Windows 📋");
        startActivity(share);finish();
    }
}
