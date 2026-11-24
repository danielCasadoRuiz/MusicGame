#import <UIKit/UIKit.h>

// Read the system trait, not a Unity view override. Called on Unity's main thread.
extern "C" int MusicGameSystemIsLight()
{
    if (@available(iOS 13.0, *))
        return [UIScreen mainScreen].traitCollection.userInterfaceStyle != UIUserInterfaceStyleDark;
    return 1;
}
