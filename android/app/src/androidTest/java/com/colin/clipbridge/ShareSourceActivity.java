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
