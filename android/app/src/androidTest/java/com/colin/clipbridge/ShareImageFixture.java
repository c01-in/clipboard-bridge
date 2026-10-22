package com.colin.clipbridge;

import android.content.*;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import java.io.*;

/** Read-only fixture whose URI permission must be granted by the source app. */
public final class ShareImageFixture extends ContentProvider {
    public boolean onCreate(){return true;}
    public String getType(Uri uri){return "view-fixture.pdf".equals(uri.getLastPathSegment())?"application/pdf":"image/png";}
    public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException{
        String name=uri.getLastPathSegment();
        if(!"r".equals(mode)||!("fixture.png".equals(name)||"large.bin".equals(name)||"empty.dat".equals(name)||"view-fixture.pdf".equals(name)))throw new FileNotFoundException();
        return ParcelFileDescriptor.open(new File(getContext().getCacheDir(),name),ParcelFileDescriptor.MODE_READ_ONLY);
    }
    public Cursor query(Uri uri,String[] projection,String selection,String[] args,String sort){
        String name=uri.getLastPathSegment();File file=new File(getContext().getCacheDir(),name==null?"":name);
        if(!("fixture.png".equals(name)||"large.bin".equals(name)||"empty.dat".equals(name)||"view-fixture.pdf".equals(name))||!file.isFile())return null;
        String[] columns=projection==null?new String[]{android.provider.OpenableColumns.DISPLAY_NAME,android.provider.OpenableColumns.SIZE}:projection;
        android.database.MatrixCursor cursor=new android.database.MatrixCursor(columns);Object[] row=new Object[columns.length];
        for(int i=0;i<columns.length;i++)row[i]=android.provider.OpenableColumns.DISPLAY_NAME.equals(columns[i])?name:android.provider.OpenableColumns.SIZE.equals(columns[i])?file.length():null;
        cursor.addRow(row);return cursor;
    }
    public Uri insert(Uri uri,ContentValues values){throw new UnsupportedOperationException();}
    public int update(Uri uri,ContentValues values,String selection,String[] args){throw new UnsupportedOperationException();}
    public int delete(Uri uri,String selection,String[] args){throw new UnsupportedOperationException();}
}
