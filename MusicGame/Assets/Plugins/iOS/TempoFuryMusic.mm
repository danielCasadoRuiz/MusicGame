#import <AVFoundation/AVFoundation.h>
#import <MediaPlayer/MediaPlayer.h>
#import <UIKit/UIKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <CommonCrypto/CommonCryptor.h>
#import <CommonCrypto/CommonHMAC.h>
#import <CommonCrypto/CommonDigest.h>
extern "C" UIViewController *UnityGetGLViewController(void);

static NSMutableData *TFOpenTps(NSString *path, NSString *encodedKey, NSError **error) {
    NSData *envelope=[NSData dataWithContentsOfFile:path options:NSDataReadingMappedIfSafe error:error];
    if (!envelope) return nil;
    NSData *keys=[[NSData alloc] initWithBase64EncodedString:encodedKey options:0];
    BOOL valid=keys.length==64 && envelope.length>=96 && envelope.length<=536871008;
    const uint8_t *bytes=(const uint8_t*)envelope.bytes;
    uint64_t length=0;
    if (valid) {
        memcpy(&length, bytes+40, 8); length=CFSwapInt64LittleToHost(length);
        NSUInteger cipherLength=envelope.length-80;
        valid=memcmp(bytes,"TPS1",4)==0 && bytes[4]==2 && bytes[5]==1 && bytes[6]==0 && bytes[7]==0 &&
            length<=536870912 && cipherLength%16==0 && length/16*16+16==cipherLength;
    }
    if (valid) {
        unsigned char tag[CC_SHA256_DIGEST_LENGTH];
        CCHmac(kCCHmacAlgSHA256, ((const uint8_t*)keys.bytes)+32, 32, bytes, envelope.length-32, tag);
        unsigned difference=0; for (NSUInteger i=0;i<32;i++) difference|=tag[i]^bytes[envelope.length-32+i];
        valid=difference==0;
    }
    if (!valid) {
        if (error) *error=[NSError errorWithDomain:@"TempoFurySound" code:1 userInfo:@{NSLocalizedDescriptionKey:@"TPS authentication failed"}];
        return nil;
    }
    NSMutableData *plain=[NSMutableData dataWithLength:envelope.length-80];
    size_t written=0;
    CCCryptorStatus result=CCCrypt(kCCDecrypt,kCCAlgorithmAES,kCCOptionPKCS7Padding,keys.bytes,32,bytes+24,bytes+48,envelope.length-80,plain.mutableBytes,plain.length,&written);
    if (result!=kCCSuccess || written!=length) {
        [plain resetBytesInRange:NSMakeRange(0,plain.length)];
        if (error) *error=[NSError errorWithDomain:@"TempoFurySound" code:2 userInfo:@{NSLocalizedDescriptionKey:@"TPS audio decode failed"}];
        return nil;
    }
    plain.length=written; return plain;
}


@interface TFMusic : NSObject <AVAudioPlayerDelegate, UIDocumentPickerDelegate>
@property(nonatomic,strong) AVAudioPlayer *player;
@property(nonatomic,strong) NSMutableData *decryptedAudio;
@property(nonatomic,strong) NSArray *tracks;
@property(nonatomic,strong) NSString *error;
@property(nonatomic,strong) NSString *picked;
@property(nonatomic,strong) NSString *previousCategory;
@property(nonatomic) NSInteger index;
@property(nonatomic) BOOL resumeAfterInterruption;
@property(nonatomic) float volume;
-(void)load:(NSInteger)index;
-(void)stop;
-(void)publish;
-(void)command:(NSString*)command;
@end

@implementation TFMusic
-(id)init {
    self = [super init];
    if (self) {
        _index = -1; _error = @""; _picked = @""; _volume = 1;
        NSNotificationCenter *center = NSNotificationCenter.defaultCenter;
        [center addObserver:self selector:@selector(interrupted:) name:AVAudioSessionInterruptionNotification object:nil];
        [center addObserver:self selector:@selector(routeChanged:) name:AVAudioSessionRouteChangeNotification object:nil];
        MPRemoteCommandCenter *remote = MPRemoteCommandCenter.sharedCommandCenter;
        [remote.playCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) { [self command:@"play"]; return MPRemoteCommandHandlerStatusSuccess; }];
        [remote.pauseCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) { [self command:@"pause"]; return MPRemoteCommandHandlerStatusSuccess; }];
        [remote.togglePlayPauseCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) { [self command:@"toggle"]; return MPRemoteCommandHandlerStatusSuccess; }];
        [remote.nextTrackCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) { [self command:@"next"]; return MPRemoteCommandHandlerStatusSuccess; }];
        [remote.previousTrackCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) { [self command:@"previous"]; return MPRemoteCommandHandlerStatusSuccess; }];
        [remote.stopCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) { [self stop]; return MPRemoteCommandHandlerStatusSuccess; }];
        [remote.changePlaybackPositionCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) {
            if (!self.player) return MPRemoteCommandHandlerStatusNoSuchContent;
            self.player.currentTime = ((MPChangePlaybackPositionCommandEvent*)event).positionTime; [self publish]; return MPRemoteCommandHandlerStatusSuccess;
        }];
        [self commandsEnabled:NO];
    }
    return self;
}
-(void)commandsEnabled:(BOOL)enabled {
    MPRemoteCommandCenter *r = MPRemoteCommandCenter.sharedCommandCenter;
    r.playCommand.enabled=enabled; r.pauseCommand.enabled=enabled; r.togglePlayPauseCommand.enabled=enabled;
    r.nextTrackCommand.enabled=enabled; r.previousTrackCommand.enabled=enabled; r.stopCommand.enabled=enabled; r.changePlaybackPositionCommand.enabled=enabled;
}
-(void)load:(NSInteger)index {
    [self.player stop]; self.player = nil;
    if (self.decryptedAudio) [self.decryptedAudio resetBytesInRange:NSMakeRange(0,self.decryptedAudio.length)];
    self.decryptedAudio=nil;
    if (index < 0 || index >= (NSInteger)self.tracks.count) { [self stop]; return; }
    self.index = index;
    NSError *error = nil;
    AVAudioSession *session = AVAudioSession.sharedInstance;
    if (!self.previousCategory) self.previousCategory = session.category;
    BOOL categoryOK = [session setCategory:AVAudioSessionCategoryPlayback mode:AVAudioSessionModeDefault options:0 error:&error];
    BOOL activeOK = categoryOK && [session setActive:YES error:&error];
    if (!activeOK) { self.error=error.localizedDescription ?: @"Audio session unavailable"; [self stop]; return; }
    NSString *path=self.tracks[index][@"path"];
    if ([path.pathExtension isEqualToString:@"tps"]) {
        self.decryptedAudio=TFOpenTps(path,self.tracks[index][@"key"],&error);
        if (self.decryptedAudio) self.player=[[AVAudioPlayer alloc] initWithData:self.decryptedAudio fileTypeHint:AVFileTypeAppleM4A error:&error];
    } else {
        // User-imported recordings only. Original game music always uses TPS.
        self.player=[[AVAudioPlayer alloc] initWithContentsOfURL:[NSURL fileURLWithPath:path] error:&error];
    }
    if (!self.player) { self.error = error.localizedDescription ?: @"Audio unavailable"; [self stop]; return; }
    self.player.delegate = self; self.player.volume=self.volume; [self.player prepareToPlay];
    if (![self.player play]) { self.error=@"Playback failed"; [self stop]; return; }
    [self commandsEnabled:YES]; [self publish];
}
-(void)publish {
    if (self.index < 0 || self.index >= (NSInteger)self.tracks.count || !self.player) return;
    NSDictionary *track = self.tracks[self.index];
    MPNowPlayingInfoCenter.defaultCenter.nowPlayingInfo = @{
        MPMediaItemPropertyTitle:track[@"title"] ?: @"", MPMediaItemPropertyArtist:track[@"artist"] ?: @"",
        MPMediaItemPropertyPlaybackDuration:@(self.player.duration), MPNowPlayingInfoPropertyElapsedPlaybackTime:@(self.player.currentTime),
        MPNowPlayingInfoPropertyPlaybackRate:@(self.player.playing ? 1 : 0)
    };
}
-(void)command:(NSString*)command {
    if ([command isEqualToString:@"stop"]) { [self stop]; return; }
    if (!self.player) return;
    if ([command isEqualToString:@"next"]) { [self load:(self.index+1)%self.tracks.count]; return; }
    if ([command isEqualToString:@"previous"]) { [self load:(self.index+self.tracks.count-1)%self.tracks.count]; return; }
    if ([command isEqualToString:@"toggle"]) command = self.player.playing ? @"pause" : @"play";
    if ([command isEqualToString:@"pause"]) { [self.player pause]; self.resumeAfterInterruption=NO; }
    if ([command isEqualToString:@"play"]) {
        NSError *error=nil;
        if ([AVAudioSession.sharedInstance setActive:YES error:&error]) [self.player play];
        else self.error=error.localizedDescription ?: @"Audio session unavailable";
    }
    [self publish];
}
-(void)stop {
    [self.player stop]; self.player=nil;
    if (self.decryptedAudio) [self.decryptedAudio resetBytesInRange:NSMakeRange(0,self.decryptedAudio.length)];
    self.decryptedAudio=nil; self.index=-1; self.resumeAfterInterruption=NO;
    MPNowPlayingInfoCenter.defaultCenter.nowPlayingInfo=nil; [self commandsEnabled:NO];
    if (self.previousCategory) {
        [AVAudioSession.sharedInstance setActive:NO withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation error:NULL];
        [AVAudioSession.sharedInstance setCategory:self.previousCategory error:NULL]; self.previousCategory=nil;
    }
}
-(void)audioPlayerDidFinishPlaying:(AVAudioPlayer*)player successfully:(BOOL)flag {
    if (flag) [self load:self.index+1]; else { self.error=@"Audio playback failed"; [self stop]; }
}
-(void)audioPlayerDecodeErrorDidOccur:(AVAudioPlayer*)player error:(NSError*)error { self.error=error.localizedDescription ?: @"Decode failed"; [self stop]; }
-(void)interrupted:(NSNotification*)note {
    if ([note.userInfo[AVAudioSessionInterruptionTypeKey] integerValue]==AVAudioSessionInterruptionTypeBegan) {
        self.resumeAfterInterruption=self.player.playing; [self.player pause]; [self publish];
    } else if (self.resumeAfterInterruption) {
        self.resumeAfterInterruption=NO;
        if ([note.userInfo[AVAudioSessionInterruptionOptionKey] unsignedIntegerValue]&AVAudioSessionInterruptionOptionShouldResume) [self command:@"play"];
    }
}
-(void)routeChanged:(NSNotification*)note {
    if ([note.userInfo[AVAudioSessionRouteChangeReasonKey] integerValue]==AVAudioSessionRouteChangeReasonOldDeviceUnavailable) [self command:@"pause"];
}
-(void)pick {
    self.picked=@"__PENDING__";
    UIDocumentPickerViewController *picker;
    if (@available(iOS 14.0,*)) picker=[[UIDocumentPickerViewController alloc] initForOpeningContentTypes:@[UTTypeAudio] asCopy:YES];
    else picker=[[UIDocumentPickerViewController alloc] initWithDocumentTypes:@[@"public.audio"] inMode:UIDocumentPickerModeImport];
    picker.delegate=self; picker.allowsMultipleSelection=NO;
    UIViewController *controller=UnityGetGLViewController();
    while (controller.presentedViewController) controller=controller.presentedViewController;
    if (controller) [controller presentViewController:picker animated:YES completion:nil]; else self.picked=@"";
}
-(void)documentPickerWasCancelled:(UIDocumentPickerViewController*)controller { self.picked=@""; }
-(void)documentPicker:(UIDocumentPickerViewController*)controller didPickDocumentsAtURLs:(NSArray<NSURL*>*)urls {
    NSURL *source=urls.firstObject;
    if (!source) { self.picked=@""; return; }
    NSString *ext=source.pathExtension.lowercaseString;
    if (![@[@"wav",@"mp3",@"ogg",@"aif",@"aiff"] containsObject:ext]) { self.picked=@""; return; }
    BOOL scoped=[source startAccessingSecurityScopedResource];
    NSString *folder=[NSTemporaryDirectory() stringByAppendingPathComponent:@"tf-audio-picker"];
    NSFileManager *files=NSFileManager.defaultManager;
    [files createDirectoryAtPath:folder withIntermediateDirectories:YES attributes:nil error:NULL];
    for (NSString *name in [files contentsOfDirectoryAtPath:folder error:NULL]) [files removeItemAtPath:[folder stringByAppendingPathComponent:name] error:NULL];
    NSString *target=[folder stringByAppendingPathComponent:source.lastPathComponent];
    NSError *error=nil;
    self.picked=[files copyItemAtPath:source.path toPath:target error:&error] ? target : @"";
    if (scoped) [source stopAccessingSecurityScopedResource];
}
@end

static TFMusic *TFShared() { static TFMusic *shared; static dispatch_once_t once; dispatch_once(&once, ^{ shared=[TFMusic new]; }); return shared; }
extern "C" {
    void TFMusicStart(const char *json, int index) {
        TFMusic *music=TFShared(); [music stop]; music.error=@"";
        NSData *data=[[NSString stringWithUTF8String:json] dataUsingEncoding:NSUTF8StringEncoding];
        NSError *error=nil; id parsed=[NSJSONSerialization JSONObjectWithData:data options:0 error:&error];
        if (![parsed isKindOfClass:NSArray.class]) { music.error=@"Invalid audio queue"; return; }
        music.tracks=parsed; [music load:index];
    }
    void TFMusicCommand(const char *command) { [TFShared() command:[NSString stringWithUTF8String:command]]; }
    int TFMusicIndex() { return (int)TFShared().index; }
    bool TFMusicPlaying() { return TFShared().player.playing; }
    float TFMusicPosition() { return (float)TFShared().player.currentTime; }
    void TFMusicVolume(float value) { TFShared().volume=value; TFShared().player.volume=value; }
    const char *TFMusicError() { return TFShared().error.UTF8String; }
    void TFMusicPick() { [TFShared() pick]; }
    const char *TFMusicPickResult() { return TFShared().picked.UTF8String; }
}
