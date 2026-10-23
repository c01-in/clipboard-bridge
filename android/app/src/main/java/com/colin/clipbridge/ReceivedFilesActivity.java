package com.colin.clipbridge;

import android.app.*;
import android.content.*;
import android.net.Uri;
import android.os.Bundle;
import android.provider.DocumentsContract;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import org.json.*;

/** Opens only completed local receipts after the user taps a notification. */
public final class ReceivedFilesActivity extends Activity {
    private JSONObject receipt;
    private boolean waitingForPicker;
    static PendingIntent action(Context context,String id,String action){
        Intent intent=new Intent(context,ReceivedFilesActivity.class).setAction(action)
            .setData(Uri.parse("clipbridge-received:"+id+"/"+action)).putExtra("batchId",id);
        return PendingIntent.getActivity(context,0,intent,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
    }
    static Notification notification(Context context,String text,String id,JSONObject receipt)throws Exception{
        JSONArray files=receipt.getJSONArray("files");
        Notification.Builder notice=Notices.builder(context,text);
        PendingIntent folder=action(context,id,"folder");
        notice.setContentIntent(files.length()==1?action(context,id,"details"):folder);
        if(files.length()==1)notice.addAction(new Notification.Action.Builder(null,context.getString(R.string.files_open_file),action(context,id,"open")).build());
        return notice.addAction(new Notification.Action.Builder(null,context.getString(R.string.files_view_location),folder).build()).build();
    }
    static JSONObject readReceipt(Context context,String id)throws Exception{
        if(id==null||!id.matches("[0-9a-f]{32}"))throw new IllegalArgumentException("Invalid receipt ID.");
        File file=new File(new File(context.getFilesDir(),"file-receipts"),id+".json");
        if(!file.isFile()||file.length()>1024*1024)throw new IllegalArgumentException("Missing receipt.");
        JSONObject receipt=new JSONObject(new String(Files.readAllBytes(file.toPath()),StandardCharsets.UTF_8));
        JSONArray entries=receipt.getJSONArray("files");
        if(!receipt.optBoolean("complete")||!receipt.optBoolean("acknowledged")||entries.length()<1||entries.length()>FileTransfer.MAX_FILES)throw new IllegalArgumentException("Incomplete receipt.");
        for(int i=0;i<entries.length();i++)if(!entries.getJSONObject(i).optBoolean("done")||!"content".equals(Uri.parse(entries.getJSONObject(i).getString("uri")).getScheme()))throw new IllegalArgumentException("Invalid saved file.");
        return receipt;
    }
    static Intent fileIntent(Context context,Uri uri){
        String type=context.getContentResolver().getType(uri);
        Intent intent=new Intent(Intent.ACTION_VIEW).setDataAndType(uri,type==null?"application/octet-stream":type)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        intent.setClipData(ClipData.newRawUri("",uri));
        return intent;
    }
    static Intent locationIntent(JSONObject receipt)throws Exception{
        String folder=receipt.optString("folder","");
        Uri initial=folder.isEmpty()?DocumentsContract.buildDocumentUri("com.android.externalstorage.documents","primary:Download/Clipboard Bridge"):
            DocumentsContract.buildDocumentUriUsingTree(Uri.parse(folder),DocumentsContract.getTreeDocumentId(Uri.parse(folder)));
        // The system picker can show a location without granting another app access
        // to an entire directory, or requesting any new storage permission.
        return new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE)
            .putExtra(DocumentsContract.EXTRA_INITIAL_URI,initial).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
    }
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);
        try{
            receipt=readReceipt(this,getIntent().getStringExtra("batchId"));
            if(saved!=null&&saved.getBoolean("waitingForPicker")){waitingForPicker=true;return;}
            getSystemService(NotificationManager.class).cancel(getIntent().getStringExtra("batchId"),23);
            if("open".equals(getIntent().getAction())&&receipt.getJSONArray("files").length()==1)
                openFile(Uri.parse(receipt.getJSONArray("files").getJSONObject(0).getString("uri")));
            else if("details".equals(getIntent().getAction())&&receipt.getJSONArray("files").length()==1)
                new AlertDialog.Builder(this).setMessage(getString(R.string.files_received_phone,"1"))
                    .setPositiveButton(R.string.files_open_file,(dialog,which)->{
                        try{openFile(Uri.parse(receipt.getJSONArray("files").getJSONObject(0).getString("uri")));}catch(Exception e){unavailable(true);}
                    }).setNeutralButton(R.string.files_view_location,(dialog,which)->showLocation())
                    .setNegativeButton(android.R.string.cancel,(dialog,which)->finish()).setOnCancelListener(dialog->finish()).show();
            else showLocation();
        }catch(Exception e){unavailable(false);}
    }
    private void openFile(Uri uri){
        try{startActivity(fileIntent(this,uri));finish();}
        catch(Exception e){unavailable(receipt!=null);}
    }
    private void showLocation(){
        try{waitingForPicker=true;startActivityForResult(locationIntent(receipt),1);}
        catch(Exception e){waitingForPicker=false;unavailable(false);}
    }
    private void unavailable(boolean offerLocation){
        AlertDialog.Builder dialog=new AlertDialog.Builder(this).setMessage(R.string.files_open_unavailable)
            .setPositiveButton(android.R.string.ok,(d,w)->finish()).setOnCancelListener(d->finish());
        if(offerLocation)dialog.setNeutralButton(R.string.files_view_location,(d,w)->showLocation());
        dialog.show();
    }
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);
        waitingForPicker=false;
        if(request==1&&result==RESULT_OK&&data!=null&&data.getData()!=null&&"content".equals(data.getData().getScheme()))openFile(data.getData());
        else finish();
    }
    @Override protected void onSaveInstanceState(Bundle state){state.putBoolean("waitingForPicker",waitingForPicker);super.onSaveInstanceState(state);}
}
