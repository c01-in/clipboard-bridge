package com.colin.clipbridge;

import android.content.*;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import java.io.*;

/** Read-only fixture whose URI permission must be granted by the source app. */
public final class ShareImageFixture extends ContentProvider {
    public boolean onCreate(){return true;}
    public String getType(Uri uri){return "image/png";}
    public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException{
        if(!"r".equals(mode)||!"/fixture.png".equals(uri.getPath()))throw new FileNotFoundException();
        return ParcelFileDescriptor.open(new File(getContext().getCacheDir(),"fixture.png"),ParcelFileDescriptor.MODE_READ_ONLY);
    }
    public Cursor query(Uri uri,String[] projection,String selection,String[] args,String sort){return null;}
    public Uri insert(Uri uri,ContentValues values){throw new UnsupportedOperationException();}
    public int update(Uri uri,ContentValues values,String selection,String[] args){throw new UnsupportedOperationException();}
    public int delete(Uri uri,String selection,String[] args){throw new UnsupportedOperationException();}
}
