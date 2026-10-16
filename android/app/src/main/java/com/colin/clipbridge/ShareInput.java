package com.colin.clipbridge;

import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import androidx.core.content.IntentCompat;

/** Reads only the content explicitly shared by the source app, never the clipboard. */
final class ShareInput {
    static ClipboardSender.Input capture(Intent intent)throws Exception {
        if(intent==null||!Intent.ACTION_SEND.equals(intent.getAction()))throw invalid();
        String type=intent.getType();
        ClipData clip=intent.getClipData();
        if(clip!=null&&clip.getItemCount()>1)throw new Exception(LocalText.get(R.string.error_one_item));
        if(type!=null&&type.startsWith("image/")){
            Uri uri=IntentCompat.getParcelableExtra(intent,Intent.EXTRA_STREAM,Uri.class);
            if(uri==null&&clip!=null&&clip.getItemCount()==1)uri=clip.getItemAt(0).getUri();
            if(uri==null||!"content".equals(uri.getScheme()))throw invalid();
            return new ClipboardSender.Input(null,uri);
        }
        if("text/plain".equals(type)){
            // Never dereference a stream URI or arbitrary file passed as text.
            if(intent.hasExtra(Intent.EXTRA_STREAM))throw invalid();
            CharSequence text=intent.getCharSequenceExtra(Intent.EXTRA_TEXT);
            if(text==null&&clip!=null&&clip.getItemCount()==1)text=clip.getItemAt(0).getText();
            if(text!=null&&text.length()>0)return new ClipboardSender.Input(text.toString(),null);
        }
        throw invalid();
    }
    private static Exception invalid(){return new Exception(LocalText.get(R.string.error_share_content));}
}
