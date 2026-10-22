package com.colin.clipbridge;

import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import androidx.core.content.IntentCompat;
import java.util.ArrayList;

/** Reads only the content explicitly shared by the source app, never the clipboard. */
final class ShareInput {
    static ArrayList<Uri> files(Intent intent)throws Exception {
        if(intent==null)throw invalid();
        ArrayList<Uri> result=new ArrayList<>();
        if(Intent.ACTION_VIEW.equals(intent.getAction())){
            if(intent.getData()==null)throw invalid();
            result.add(intent.getData());
        }else if(Intent.ACTION_SEND_MULTIPLE.equals(intent.getAction())){
            ArrayList<Uri> streams=IntentCompat.getParcelableArrayListExtra(intent,Intent.EXTRA_STREAM,Uri.class);
            if(streams!=null)result.addAll(streams);
        }else if(Intent.ACTION_SEND.equals(intent.getAction())){
            Uri stream=IntentCompat.getParcelableExtra(intent,Intent.EXTRA_STREAM,Uri.class);if(stream!=null)result.add(stream);
        }else throw invalid();
        if(result.isEmpty()&&intent.getClipData()!=null){
            ClipData clip=intent.getClipData();
            for(int i=0;i<clip.getItemCount();i++){
                Uri uri=clip.getItemAt(i).getUri();if(uri!=null)result.add(uri);
                else if(Intent.ACTION_SEND_MULTIPLE.equals(intent.getAction()))throw invalid();
            }
        }
        if(result.size()>FileTransfer.MAX_FILES)throw new Exception(LocalText.get(R.string.files_selection_limit));
        for(Uri uri:result)if(uri==null||!"content".equals(uri.getScheme()))throw invalid();
        return result;
    }
    static boolean asFiles(Intent intent)throws Exception {
        ArrayList<Uri> uris=files(intent);
        return !uris.isEmpty()&&(Intent.ACTION_VIEW.equals(intent.getAction())||intent.getBooleanExtra("asFiles",false)||uris.size()>1||Intent.ACTION_SEND_MULTIPLE.equals(intent.getAction())||intent.getType()==null||!intent.getType().startsWith("image/"));
    }
    static ClipData grants(ArrayList<Uri> uris){
        ClipData clip=ClipData.newRawUri("",uris.get(0));
        for(int i=1;i<uris.size();i++)clip.addItem(new ClipData.Item(uris.get(i)));
        return clip;
    }
    /** Forward exactly the selected content, with a read grant, to the file sender. */
    static Intent fileIntent(Intent source)throws Exception{
        ArrayList<Uri> uris=files(source);if(uris.isEmpty())throw invalid();
        Intent intent=new Intent(uris.size()==1?Intent.ACTION_SEND:Intent.ACTION_SEND_MULTIPLE).setType(source.getType())
            .putExtra("asFiles",true).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        intent.setClipData(grants(uris));
        if(uris.size()==1)intent.putExtra(Intent.EXTRA_STREAM,uris.get(0));else intent.putParcelableArrayListExtra(Intent.EXTRA_STREAM,uris);
        return intent;
    }
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
