package com.colin.clipbridge;

import android.content.Intent;
import android.net.Uri;
import androidx.test.ext.junit.runners.AndroidJUnit4;
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
}
