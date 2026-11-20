/* i26ogv: see i26ogv.h. Demuxing follows libtheora's examples/player_example.c: pages from the file go to the Theora
 * and Vorbis stream states; reading a video frame leaves the sound's packets queued in theirs and the other way round,
 * so the caller can take the two at different paces from the one file. */
#include "i26ogv.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <ogg/ogg.h>
#include <vorbis/codec.h>
#include <theora/theoradec.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#  define i26_fseek _fseeki64
#  define i26_ftell _ftelli64
#else
#  define i26_fseek fseeko
#  define i26_ftell ftello
#endif

struct i26ogv {
    FILE *f;
    ogg_sync_state oy;
    ogg_stream_state to, vo;
    int has_t, has_v;      /* a stream of each kind found (its headers all read once open succeeds) */
    th_info ti;
    th_comment tc;
    th_setup_info *ts;
    th_dec_ctx *td;
    vorbis_info vi;
    vorbis_comment vc;
    vorbis_dsp_state vd;
    vorbis_block vb;
    int v_ready;           /* vd and vb made */
    int eof;
    int64_t next_index;    /* the next frame's number, where the stream gives no granule position */
    int have_frame;        /* a picture has been decoded (a duplicate frame repeats it) */
    th_ycbcr_buffer ycbcr;
    int64_t frame_count, audio_frames;
};

int32_t i26ogv_abi(void) { return I26OGV_ABI; }

const char *i26ogv_versions(void)
{
    static char text[256];
    if (!text[0])
        snprintf(text, sizeof text, "libogg 1.3.6; libvorbis 1.3.7 (%s); libtheora 1.2.0 (%s)", vorbis_version_string(), th_version_string());
    return text;
}

static FILE *open_utf8(const char *path)
{
#if defined(_WIN32)
    int n = MultiByteToWideChar(CP_UTF8, 0, path, -1, NULL, 0);
    if (n <= 0) return NULL;
    wchar_t *w = (wchar_t *)malloc(sizeof(wchar_t) * (size_t)n);
    if (!w) return NULL;
    MultiByteToWideChar(CP_UTF8, 0, path, -1, w, n);
    FILE *f = _wfopen(w, L"rb");
    free(w);
    return f;
#else
    return fopen(path, "rb");
#endif
}

/* Reads file data until a whole page is out; returns 1 with it in *og, 0 at the end of the file. */
static int next_page(i26ogv *h, ogg_page *og)
{
    while (ogg_sync_pageout(&h->oy, og) != 1) {
        char *buf = ogg_sync_buffer(&h->oy, 65536);
        size_t n = fread(buf, 1, 65536, h->f);
        if (n == 0) { h->eof = 1; return 0; }
        ogg_sync_wrote(&h->oy, (long)n);
    }
    return 1;
}

/* One more page into the streams it belongs to (stream states take only their own serial's pages). */
static int pull_page(i26ogv *h)
{
    ogg_page og;
    if (!next_page(h, &og)) return 0;
    if (h->has_t) ogg_stream_pagein(&h->to, &og);
    if (h->has_v) ogg_stream_pagein(&h->vo, &og);
    return 1;
}

static void free_codecs(i26ogv *h)
{
    if (h->v_ready) { vorbis_block_clear(&h->vb); vorbis_dsp_clear(&h->vd); h->v_ready = 0; }
    if (h->has_v) { ogg_stream_clear(&h->vo); h->has_v = 0; }
    vorbis_comment_clear(&h->vc);
    vorbis_info_clear(&h->vi);
    if (h->td) { th_decode_free(h->td); h->td = NULL; }
    if (h->ts) { th_setup_free(h->ts); h->ts = NULL; }
    if (h->has_t) { ogg_stream_clear(&h->to); h->has_t = 0; }
    th_comment_clear(&h->tc);
    th_info_clear(&h->ti);
    ogg_sync_clear(&h->oy);
}

/* From the file's start: the streams' headers, then the decoders. Returns 0 or < 0. */
static int start(i26ogv *h)
{
    ogg_page og;
    ogg_packet op;
    int t_hdr = 0, v_hdr = 0;
    ogg_sync_init(&h->oy);
    th_info_init(&h->ti);
    th_comment_init(&h->tc);
    vorbis_info_init(&h->vi);
    vorbis_comment_init(&h->vc);
    h->eof = 0;
    h->next_index = 0;
    h->have_frame = 0;
    if (i26_fseek(h->f, 0, SEEK_SET) != 0) return -1;

    /* the beginning-of-stream pages: one per stream, all before any data */
    for (;;) {
        if (!next_page(h, &og)) break;
        if (!ogg_page_bos(&og)) {
            /* the first data page: into whichever stream it is for */
            if (h->has_t) ogg_stream_pagein(&h->to, &og);
            if (h->has_v) ogg_stream_pagein(&h->vo, &og);
            break;
        }
        ogg_stream_state test;
        ogg_stream_init(&test, ogg_page_serialno(&og));
        ogg_stream_pagein(&test, &og);
        if (ogg_stream_packetpeek(&test, &op) != 1) { ogg_stream_clear(&test); continue; }
        if (!h->has_t && th_decode_headerin(&h->ti, &h->tc, &h->ts, &op) >= 0) {
            memcpy(&h->to, &test, sizeof test);
            ogg_stream_packetout(&h->to, NULL);
            h->has_t = 1; t_hdr = 1;
        } else if (!h->has_v && vorbis_synthesis_headerin(&h->vi, &h->vc, &op) >= 0) {
            memcpy(&h->vo, &test, sizeof test);
            ogg_stream_packetout(&h->vo, NULL);
            h->has_v = 1; v_hdr = 1;
        } else ogg_stream_clear(&test);
    }
    if (!h->has_t) return -2;

    /* the rest of the headers (three packets each) */
    while ((h->has_t && t_hdr < 3) || (h->has_v && v_hdr < 3)) {
        int got = 0;
        while (h->has_t && t_hdr < 3 && ogg_stream_packetpeek(&h->to, &op) == 1) {
            int r = th_decode_headerin(&h->ti, &h->tc, &h->ts, &op);
            if (r < 0) return -3;
            if (r == 0) { t_hdr = 3; break; }   /* the first video packet (left queued): the headers are done */
            ogg_stream_packetout(&h->to, NULL);
            t_hdr++; got = 1;
        }
        while (h->has_v && v_hdr < 3 && ogg_stream_packetpeek(&h->vo, &op) == 1) {
            if (vorbis_synthesis_headerin(&h->vi, &h->vc, &op) < 0) return -4;
            ogg_stream_packetout(&h->vo, NULL);
            v_hdr++; got = 1;
        }
        if (got) continue;
        if (!pull_page(h)) return -5;
    }
    h->td = th_decode_alloc(&h->ti, h->ts);
    if (!h->td) return -6;
    if (h->has_v) {
        if (vorbis_synthesis_init(&h->vd, &h->vi) != 0) return -7;
        vorbis_block_init(&h->vd, &h->vb);
        h->v_ready = 1;
    }
    return 0;
}

/* The last granule positions of the two streams, from pages near the end of the file. */
static void scan_tail(i26ogv *h)
{
    h->frame_count = -1;
    h->audio_frames = -1;
    int t_serial = h->to.serialno, v_serial = h->has_v ? h->vo.serialno : 0;
    if (i26_fseek(h->f, 0, SEEK_END) != 0) return;
    int64_t size = i26_ftell(h->f);
    for (int64_t span = 256 * 1024; ; span *= 4) {
        int64_t at = size > span ? size - span : 0;
        ogg_sync_state sy;
        ogg_sync_init(&sy);
        i26_fseek(h->f, at, SEEK_SET);
        ogg_int64_t tg = -1, vg = -1;
        for (;;) {
            ogg_page og;
            int r = ogg_sync_pageout(&sy, &og);
            if (r == 1) {
                ogg_int64_t g = ogg_page_granulepos(&og);
                if (g >= 0 && ogg_page_serialno(&og) == t_serial) tg = g;
                if (g >= 0 && h->has_v && ogg_page_serialno(&og) == v_serial) vg = g;
                continue;
            }
            if (r < 0) continue;
            char *buf = ogg_sync_buffer(&sy, 65536);
            size_t n = fread(buf, 1, 65536, h->f);
            if (n == 0) break;
            ogg_sync_wrote(&sy, (long)n);
        }
        ogg_sync_clear(&sy);
        if (tg >= 0 && h->frame_count < 0) h->frame_count = th_granule_frame(h->td, tg) + 1;
        if (vg >= 0 && h->audio_frames < 0) h->audio_frames = vg;
        if ((h->frame_count >= 0 && (!h->has_v || h->audio_frames >= 0)) || at == 0) break;
    }
}

i26ogv *i26ogv_open(const char *path_utf8)
{
    if (!path_utf8) return NULL;
    i26ogv *h = (i26ogv *)calloc(1, sizeof *h);
    if (!h) return NULL;
    h->f = open_utf8(path_utf8);
    if (!h->f) { free(h); return NULL; }
    if (start(h) != 0) { i26ogv_close(h); return NULL; }
    scan_tail(h);
    /* back to where the headers left the file: re-read from the start (the stream states hold what was paged in) */
    free_codecs(h);
    if (start(h) != 0) { i26ogv_close(h); return NULL; }
    return h;
}

int32_t i26ogv_info_get(const i26ogv *h, i26ogv_info *o)
{
    if (!h || !o) return -1;
    o->width = (int32_t)h->ti.pic_width;
    o->height = (int32_t)h->ti.pic_height;
    o->frame_width = (int32_t)h->ti.frame_width;
    o->frame_height = (int32_t)h->ti.frame_height;
    o->pic_x = (int32_t)h->ti.pic_x;
    o->pic_y = (int32_t)h->ti.pic_y;
    o->fps_num = (int32_t)h->ti.fps_numerator;
    o->fps_den = (int32_t)h->ti.fps_denominator;
    o->pixel_fmt = (int32_t)h->ti.pixel_fmt;
    o->colorspace = (int32_t)h->ti.colorspace;
    o->audio_rate = h->has_v ? (int32_t)h->vi.rate : 0;
    o->audio_channels = h->has_v ? (int32_t)h->vi.channels : 0;
    o->frame_count = h->frame_count;
    o->audio_frames = h->audio_frames;
    return 0;
}

/* The next picture into h->ycbcr. Returns 1, 0 at the end, < 0 on an error. */
static int decode_next(i26ogv *h, int64_t *index, double *seconds)
{
    ogg_packet op;
    for (;;) {
        int r = ogg_stream_packetout(&h->to, &op);
        if (r > 0) {
            ogg_int64_t gp = -1;
            int e = th_decode_packetin(h->td, &op, &gp);
            if (e != 0 && e != TH_DUPFRAME) continue;   /* a bad packet: on to the next */
            if (e == 0) { th_decode_ycbcr_out(h->td, h->ycbcr); h->have_frame = 1; }
            if (!h->have_frame) continue;                /* a duplicate before any picture: nothing to repeat */
            int64_t i = gp >= 0 ? (int64_t)th_granule_frame(h->td, gp) : h->next_index;
            if (i < 0) i = h->next_index;
            h->next_index = i + 1;
            if (index) *index = i;
            if (seconds) *seconds = h->ti.fps_numerator ? (double)i * h->ti.fps_denominator / h->ti.fps_numerator : 0;
            return 1;
        }
        if (r < 0) continue;   /* a hole in the data */
        if (!pull_page(h)) return 0;
    }
}

static void copy_plane(uint8_t *dst, int32_t stride, const th_img_plane *p, int x, int y, int w, int hgt)
{
    const unsigned char *src = p->data + (ptrdiff_t)y * p->stride + x;
    for (int r = 0; r < hgt; r++) memcpy(dst + (ptrdiff_t)r * stride, src + (ptrdiff_t)r * p->stride, (size_t)w);
}

int32_t i26ogv_next_frame(i26ogv *h, uint8_t *y, int32_t ys, uint8_t *cb, int32_t cbs, uint8_t *cr, int32_t crs, int64_t *index, double *seconds)
{
    if (!h || !h->td) return -1;
    int r = decode_next(h, index, seconds);
    if (r <= 0) return r;
    int px = (int)h->ti.pic_x, py = (int)h->ti.pic_y, pw = (int)h->ti.pic_width, ph = (int)h->ti.pic_height;
    int xs = !(h->ti.pixel_fmt & 1), ys2 = !(h->ti.pixel_fmt & 2);   /* chroma halved across / down */
    int cx = px >> xs, cy = py >> ys2, cw = ((px + pw + xs) >> xs) - cx, ch = ((py + ph + ys2) >> ys2) - cy;
    if (y) copy_plane(y, ys, &h->ycbcr[0], px, py, pw, ph);
    if (cb) copy_plane(cb, cbs, &h->ycbcr[1], cx, cy, cw, ch);
    if (cr) copy_plane(cr, crs, &h->ycbcr[2], cx, cy, cw, ch);
    return 1;
}

static uint8_t clamp8(int v) { return (uint8_t)(v < 0 ? 0 : v > 255 ? 255 : v); }

/* A chroma sample at a picture pixel: bilinear between the centred samples (the half-size plane's own grid). */
static int chroma_at(const th_img_plane *p, int lx, int ly, int xs, int ys, int cw0, int ch0, int cw, int ch)
{
    /* the luma pixel's centre in chroma texels, less half a texel: weights in quarters for a halved axis */
    int ax, bx, wx, ay, by, wy;   /* wx, wy: the weight of b (of 4) */
    if (xs) { int k = lx >> 1; if (lx & 1) { ax = k; bx = k + 1; wx = 1; } else { ax = k - 1; bx = k; wx = 3; } }
    else { ax = bx = lx; wx = 0; }
    if (ys) { int k = ly >> 1; if (ly & 1) { ay = k; by = k + 1; wy = 1; } else { ay = k - 1; by = k; wy = 3; } }
    else { ay = by = ly; wy = 0; }
    if (ax < cw0) ax = cw0; if (bx < cw0) bx = cw0; if (ax > cw - 1) ax = cw - 1; if (bx > cw - 1) bx = cw - 1;
    if (ay < ch0) ay = ch0; if (by < ch0) by = ch0; if (ay > ch - 1) ay = ch - 1; if (by > ch - 1) by = ch - 1;
    const unsigned char *ra = p->data + (ptrdiff_t)ay * p->stride, *rb = p->data + (ptrdiff_t)by * p->stride;
    int top = ra[ax] * (4 - wx) + ra[bx] * wx, bot = rb[ax] * (4 - wx) + rb[bx] * wx;
    return top * (4 - wy) + bot * wy;   /* sixteen times the sample */
}

int32_t i26ogv_next_frame_rgba(i26ogv *h, uint8_t *rgba, int32_t stride, int64_t *index, double *seconds)
{
    if (!h || !h->td) return -1;
    int r = decode_next(h, index, seconds);
    if (r <= 0 || !rgba) return r;
    int px = (int)h->ti.pic_x, py = (int)h->ti.pic_y, pw = (int)h->ti.pic_width, ph = (int)h->ti.pic_height;
    int xs = !(h->ti.pixel_fmt & 1), ys = !(h->ti.pixel_fmt & 2);
    int cw0 = px >> xs, ch0 = py >> ys, cw = (px + pw + xs) >> xs, ch = (py + ph + ys) >> ys;
    for (int j = 0; j < ph; j++) {
        const unsigned char *yr = h->ycbcr[0].data + (ptrdiff_t)(py + j) * h->ycbcr[0].stride + px;
        uint8_t *o = rgba + (ptrdiff_t)j * stride;
        for (int i = 0; i < pw; i++, o += 4) {
            /* BT.601 limited range, fixed point (x 2^16 for the coefficients, chroma in sixteenths) */
            int c = (yr[i] - 16) * 76309;
            int u = chroma_at(&h->ycbcr[1], px + i, py + j, xs, ys, cw0, ch0, cw, ch) - 128 * 16;
            int v = chroma_at(&h->ycbcr[2], px + i, py + j, xs, ys, cw0, ch0, cw, ch) - 128 * 16;
            o[0] = clamp8((c + 104597 / 16 * v + 32768) >> 16);
            o[1] = clamp8((c - 25675 / 16 * u - 53279 / 16 * v + 32768) >> 16);
            o[2] = clamp8((c + 132201 / 16 * u + 32768) >> 16);
            o[3] = 255;
        }
    }
    return 1;
}

int32_t i26ogv_read_audio(i26ogv *h, float *out, int32_t max_frames)
{
    if (!h || !h->v_ready || !out || max_frames <= 0) return -1;
    int ch = h->vi.channels;
    ogg_packet op;
    for (;;) {
        float **pcm;
        int n = vorbis_synthesis_pcmout(&h->vd, &pcm);
        if (n > 0) {
            int m = n < max_frames ? n : max_frames;
            for (int i = 0; i < m; i++)
                for (int c = 0; c < ch; c++) {
                    float s = pcm[c][i];
                    out[i * ch + c] = s > 1.f ? 1.f : s < -1.f ? -1.f : s;
                }
            vorbis_synthesis_read(&h->vd, m);
            return m;
        }
        int r = ogg_stream_packetout(&h->vo, &op);
        if (r > 0) { if (vorbis_synthesis(&h->vb, &op) == 0) vorbis_synthesis_blockin(&h->vd, &h->vb); continue; }
        if (r < 0) continue;
        if (!pull_page(h)) return 0;
    }
}

int32_t i26ogv_rewind(i26ogv *h)
{
    if (!h || !h->f) return -1;
    free_codecs(h);
    return start(h) == 0 ? 0 : -2;
}

void i26ogv_close(i26ogv *h)
{
    if (!h) return;
    free_codecs(h);
    if (h->f) fclose(h->f);
    free(h);
}
