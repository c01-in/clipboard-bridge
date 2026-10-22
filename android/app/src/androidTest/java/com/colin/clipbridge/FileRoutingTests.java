package com.colin.clipbridge;

import android.content.Intent;
import android.content.ClipData;
import android.content.pm.PackageManager;
import android.net.Uri;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.util.ArrayList;
import java.util.Arrays;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public final class FileRoutingTests {
    @Test public void oneImageAndExplicitFileModeRemainDistinct()throws Exception{
        Uri image=Uri.parse("content://test.example/photo.png");
        Intent share=new Intent(Intent.ACTION_SEND).setType("image/png").putExtra(Intent.EXTRA_STREAM,image);
        assertFalse(ShareInput.asFiles(share));assertEquals(image,ShareInput.capture(share).image);
        share.putExtra("asFiles",true);assertTrue(ShareInput.asFiles(share));
    }
    @Test public void multiplePhotosVideosAndDocumentsUseFiles()throws Exception{
        ArrayList<Uri> photos=new ArrayList<>(Arrays.asList(Uri.parse("content://test.example/one"),Uri.parse("content://test.example/two")));
        Intent multiple=new Intent(Intent.ACTION_SEND_MULTIPLE).setType("image/*").putParcelableArrayListExtra(Intent.EXTRA_STREAM,photos);
        assertTrue(ShareInput.asFiles(multiple));assertEquals(2,ShareInput.files(multiple).size());
        for(String mime:new String[]{"video/mp4","application/pdf","application/zip","text/plain"})
            assertTrue(ShareInput.asFiles(new Intent(Intent.ACTION_SEND).setType(mime).putExtra(Intent.EXTRA_STREAM,photos.get(0))));
    }
    @Test public void plainTextIsNotAnAttachmentAndRawFilePathsAreRejected()throws Exception{
        assertFalse(ShareInput.asFiles(new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT,"manual text")));
        try{ShareInput.files(new Intent(Intent.ACTION_SEND).setType("application/octet-stream").putExtra(Intent.EXTRA_STREAM,Uri.parse("file:///data/private")));fail("Raw paths accepted");}
        catch(Exception expected){assertNotNull(expected.getMessage());}
    }
    @Test public void openWithUsesOnlyItsDataUriAndAlwaysSendsAFile()throws Exception{
        Uri selected=Uri.parse("content://test.example/selected.png"),unselected=Uri.parse("content://test.example/unselected");
        Intent view=new Intent(Intent.ACTION_VIEW).setDataAndType(selected,"image/png").putExtra(Intent.EXTRA_STREAM,unselected)
            .addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        view.setClipData(ClipData.newRawUri("unselected",unselected));
        assertTrue(ShareInput.asFiles(view));assertEquals(Arrays.asList(selected),ShareInput.files(view));
        Intent forwarded=ShareInput.fileIntent(view);
        assertEquals(Intent.ACTION_SEND,forwarded.getAction());assertNull(forwarded.getData());
        assertTrue(ShareInput.asFiles(forwarded));assertEquals(Arrays.asList(selected),ShareInput.files(forwarded));
        assertEquals(1,forwarded.getClipData().getItemCount());assertEquals(selected,forwarded.getClipData().getItemAt(0).getUri());
        assertEquals(Intent.FLAG_GRANT_READ_URI_PERMISSION,forwarded.getFlags());
        assertTrue(ShareInput.asFiles(new Intent(Intent.ACTION_VIEW).setDataAndType(selected,"text/plain")));
    }
    @Test public void openWithRejectsLinksRawPathsAndMissingData()throws Exception{
        for(Uri uri:new Uri[]{null,Uri.parse("https://example.test/document.pdf"),Uri.parse("file:///data/private")}){
            Intent view=new Intent(Intent.ACTION_VIEW).setDataAndType(uri,"application/pdf");
            view.setClipData(ClipData.newRawUri("not the selected file",Uri.parse("content://test.example/other")));
            try{ShareInput.fileIntent(view);fail("Unsupported VIEW source accepted");}catch(Exception expected){assertNotNull(expected.getMessage());}
        }
    }
    @Test public void systemResolversUseActionNameAndKeepLauncherName(){
        android.content.Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();
        PackageManager pm=context.getPackageManager();
        for(String mime:new String[]{"application/pdf","image/png","video/mp4","text/plain","application/octet-stream"}){
            Intent view=new Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse("content://test.example/file"),mime).setPackage(context.getPackageName());
            java.util.List<android.content.pm.ResolveInfo> matches=pm.queryIntentActivities(view,PackageManager.MATCH_DEFAULT_ONLY);
            assertEquals(1,matches.size());assertEquals(ShareActivity.class.getName(),matches.get(0).activityInfo.name);
            assertEquals(context.getString(R.string.send_to_computer),matches.get(0).loadLabel(pm).toString());
        }
        for(String action:new String[]{Intent.ACTION_SEND,Intent.ACTION_SEND_MULTIPLE}){
            java.util.List<android.content.pm.ResolveInfo> matches=pm.queryIntentActivities(new Intent(action).setType("*/*").setPackage(context.getPackageName()),PackageManager.MATCH_DEFAULT_ONLY);
            assertEquals(1,matches.size());assertEquals(context.getString(R.string.send_to_computer),matches.get(0).loadLabel(pm).toString());
        }
        assertEquals(1,pm.queryIntentActivities(new Intent(Intent.ACTION_VIEW).setData(Uri.parse("content://test.example/unknown-type")).setPackage(context.getPackageName()),PackageManager.MATCH_DEFAULT_ONLY).size());
        assertEquals(context.getString(R.string.app_name),context.getApplicationInfo().loadLabel(pm).toString());
        for(String scheme:new String[]{"https","file"})assertTrue(pm.queryIntentActivities(new Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse(scheme+"://example.test/file.pdf"),"application/pdf").setPackage(context.getPackageName()),PackageManager.MATCH_DEFAULT_ONLY).isEmpty());
    }
}
