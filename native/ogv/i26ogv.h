/* i26ogv: a small Ogg Theora + Vorbis file decoder for the game's movie player (game/Scripts/Ui/MoviePlayer.cs).
 * One handle is used by one thread at a time. Built on the reference libraries libogg, libvorbis and libtheora
 * (BSD-style licences, native/ogv/LICENSES); see native/ogv/build.ps1. */
#ifndef I26OGV_H
#define I26OGV_H

#include <stdint.h>

#if defined(_WIN32)
#  define I26OGV_API __declspec(dllexport)
#else
#  define I26OGV_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* The C API's version: bumped when a signature or the info struct changes. */
#define I26OGV_ABI 1

typedef struct i26ogv i26ogv;

typedef struct i26ogv_info {
    int32_t width, height;             /* the picture (the visible region of the coded frame) */
    int32_t frame_width, frame_height; /* the coded frame (multiples of 16) */
    int32_t pic_x, pic_y;              /* the picture's offset in the coded frame */
    int32_t fps_num, fps_den;          /* frame rate as a fraction */
    int32_t pixel_fmt;                 /* 0: 4:2:0, 2: 4:2:2, 3: 4:4:4 (TH_PF_*) */
    int32_t colorspace;                /* TH_CS_*: 0 unspecified, 1 Rec. 470M, 2 Rec. 470BG */
    int32_t audio_rate;                /* Hz, 0 with no Vorbis stream */
    int32_t audio_channels;            /* 0 with no Vorbis stream */
    int64_t frame_count;               /* from the last page's granule position, -1 when unknown */
    int64_t audio_frames;              /* likewise, samples per channel */
} i26ogv_info;

/* The API's version (I26OGV_ABI). */
I26OGV_API int32_t i26ogv_abi(void);
/* The libraries inside, as a static string ("libogg 1.3.6; libvorbis 1.3.7 ...; libtheora 1.2.0 ..."). */
I26OGV_API const char *i26ogv_versions(void);

/* Opens a file (UTF-8 path) and reads its headers; NULL when it cannot be opened or has no Theora stream. */
I26OGV_API i26ogv *i26ogv_open(const char *path_utf8);
I26OGV_API int32_t i26ogv_info_get(const i26ogv *h, i26ogv_info *out);

/* Decodes the next video frame. Its planes (the picture region only) are copied into the caller's buffers, each row
 * at its stride: Y at the picture's size, Cb and Cr at the chroma's ((w+1)/2 x (h+1)/2 for 4:2:0). NULL planes:
 * decoded but not copied (a frame whose turn has passed). *index is the frame's number from 0, *seconds its time.
 * Returns 1 for a frame, 0 at the end of the stream, < 0 on an error. */
I26OGV_API int32_t i26ogv_next_frame(i26ogv *h, uint8_t *y, int32_t y_stride, uint8_t *cb, int32_t cb_stride,
                                     uint8_t *cr, int32_t cr_stride, int64_t *index, double *seconds);

/* As i26ogv_next_frame, the picture converted to RGBA8 (BT.601 limited range, chroma bilinear from centred samples,
 * alpha 255) at the stride given. NULL rgba: decoded but not converted. */
I26OGV_API int32_t i26ogv_next_frame_rgba(i26ogv *h, uint8_t *rgba, int32_t stride, int64_t *index, double *seconds);

/* Decoded sound as interleaved float PCM (-1..1), up to max_frames frames (samples per channel).
 * Returns the frames written, 0 at the end of the sound, < 0 on an error (or no Vorbis stream). */
I26OGV_API int32_t i26ogv_read_audio(i26ogv *h, float *out, int32_t max_frames);

/* Back to the first frame and sample. Returns 0, or < 0 on an error (the handle is then only good to close). */
I26OGV_API int32_t i26ogv_rewind(i26ogv *h);

I26OGV_API void i26ogv_close(i26ogv *h);

#ifdef __cplusplus
}
#endif

#endif
