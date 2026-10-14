package com.colin.clipbridge;
import android.app.Application;
public class BridgeApplication extends Application {
    @Override public void onCreate(){super.onCreate();try{Config.initialize(this);}catch(Exception ignored){}}
}
