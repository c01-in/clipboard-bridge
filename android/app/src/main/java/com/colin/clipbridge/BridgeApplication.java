package com.colin.clipbridge;
import android.app.Application;
public class BridgeApplication extends Application {
    static android.content.Context context;
    @Override public void onCreate(){super.onCreate();context=getApplicationContext();}
}
