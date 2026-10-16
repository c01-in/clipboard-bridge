package com.colin.clipbridge;

final class LocalText {
    static String get(int resource,Object... args){return BridgeApplication.context.getString(resource,args);}
}
