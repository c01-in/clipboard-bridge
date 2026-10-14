package com.colin.clipbridge;
import android.content.*;
import android.database.*;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.*;
public class ImageProvider extends ContentProvider {
    public boolean onCreate(){return true;}
    private File file(Uri uri)throws FileNotFoundException{String name=uri.getLastPathSegment();if(name==null||!name.matches("[0-9a-f]{32}\\.png")||!uri.getPath().equals("/"+name))throw new FileNotFoundException();File f=new File(new File(getContext().getFilesDir(),"clips"),name);if(!f.isFile())throw new FileNotFoundException();return f;}
    public String getType(Uri uri){return "image/png";}
    public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException{if(!"r".equals(mode))throw new FileNotFoundException("只读");return ParcelFileDescriptor.open(file(uri),ParcelFileDescriptor.MODE_READ_ONLY);}
    public Cursor query(Uri uri,String[] projection,String selection,String[] args,String sort){try{File f=file(uri);String[] cols=projection==null?new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE}:projection;MatrixCursor cursor=new MatrixCursor(cols);Object[] row=new Object[cols.length];for(int i=0;i<cols.length;i++)row[i]=OpenableColumns.DISPLAY_NAME.equals(cols[i])?f.getName():OpenableColumns.SIZE.equals(cols[i])?f.length():null;cursor.addRow(row);return cursor;}catch(FileNotFoundException e){return null;}}
    public Uri insert(Uri uri,ContentValues values){throw new UnsupportedOperationException();}public int update(Uri uri,ContentValues values,String selection,String[] args){throw new UnsupportedOperationException();}public int delete(Uri uri,String selection,String[] args){throw new UnsupportedOperationException();}
}
