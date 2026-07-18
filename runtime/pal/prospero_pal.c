// prospero_pal.c - platform layer for the ahead-of-time runtime.
// Copyright (C) 2026 SvenGDK
//
// The ahead-of-time runtime is built against a small set of operating-system primitives. On this
// device those primitives are provided by the kernel module rather than by a general C library, so
// this source implements the POSIX surface the runtime calls and forwards each call to the kernel
// entry that backs it. The runtime archives link against this object; nothing here is specific to
// any one application.
//
// Prototypes for the kernel entries come from the device kernel headers. The forwarding is thin: the
// POSIX types line up with the kernel types on this platform, so most calls pass their arguments
// through unchanged.

#include <stdint.h>
#include <stddef.h>
#include <errno.h>

// ---------------------------------------------------------------------------
// Kernel entries this layer forwards to.
// ---------------------------------------------------------------------------

typedef int64_t sce_off_t;

extern int sceKernelMapNamedFlexibleMemory(void **addr, size_t len, int prot, int flags, const char *name);
extern int sceKernelMunmap(void *addr, size_t len);
extern int sceKernelMprotect(void *addr, size_t len, int prot);
extern size_t sceKernelGetDirectMemorySize(void);

extern int scePthreadCreate(void **thread, const void *attr, void *(*entry)(void *), void *arg, const char *name);
extern int scePthreadJoin(void *thread, void **value);
extern int scePthreadDetach(void *thread);
extern void scePthreadExit(void *value);
extern void *scePthreadSelf(void);
extern int scePthreadYield(void);

extern int scePthreadMutexInit(void **mutex, const void *attr, const char *name);
extern int scePthreadMutexDestroy(void **mutex);
extern int scePthreadMutexLock(void **mutex);
extern int scePthreadMutexTrylock(void **mutex);
extern int scePthreadMutexUnlock(void **mutex);

extern int scePthreadCondInit(void **cond, const void *attr, const char *name);
extern int scePthreadCondDestroy(void **cond);
extern int scePthreadCondWait(void **cond, void **mutex);
extern int scePthreadCondSignal(void **cond);
extern int scePthreadCondBroadcast(void **cond);

extern int scePthreadKeyCreate(unsigned int *key, void (*destructor)(void *));
extern int scePthreadKeyDelete(unsigned int key);
extern int scePthreadSetspecific(unsigned int key, const void *value);
extern void *scePthreadGetspecific(unsigned int key);

extern int sceKernelClockGettime(int clockId, struct { int64_t tv_sec; int64_t tv_nsec; } *tp);
extern int sceKernelUsleep(unsigned int microseconds);
extern int64_t sceKernelGetProcessTime(void);

// ---------------------------------------------------------------------------
// Memory.
// ---------------------------------------------------------------------------

#define PROT_NONE 0x00
#define PROT_READ 0x01
#define PROT_WRITE 0x02
#define PROT_EXEC 0x04
#define MAP_FAILED ((void *)-1)

// The runtime maps anonymous, CPU-readable/writable memory for the managed heap and for its own
// bookkeeping. Flexible memory is the general-purpose region for this; the direct-memory path is
// reserved for graphics buffers and stays in the SDK, not here.
void *pal_mmap(void *hint, size_t length, int prot)
{
    (void)hint;
    void *addr = 0;
    int kprot = 0;
    if (prot & PROT_READ) kprot |= 0x01;
    if (prot & PROT_WRITE) kprot |= 0x02;
    if (prot & PROT_EXEC) kprot |= 0x04;
    int rc = sceKernelMapNamedFlexibleMemory(&addr, length, kprot, 0, "sharpprospero-heap");
    if (rc != 0)
        return MAP_FAILED;
    return addr;
}

int pal_munmap(void *addr, size_t length)
{
    return sceKernelMunmap(addr, length) == 0 ? 0 : -1;
}

int pal_mprotect(void *addr, size_t length, int prot)
{
    int kprot = 0;
    if (prot & PROT_READ) kprot |= 0x01;
    if (prot & PROT_WRITE) kprot |= 0x02;
    if (prot & PROT_EXEC) kprot |= 0x04;
    return sceKernelMprotect(addr, length, kprot) == 0 ? 0 : -1;
}

// ---------------------------------------------------------------------------
// Threads.
// ---------------------------------------------------------------------------

int pal_thread_create(void **thread, void *(*entry)(void *), void *arg)
{
    return scePthreadCreate(thread, 0, entry, arg, "sharpprospero");
}

int pal_thread_join(void *thread, void **value) { return scePthreadJoin(thread, value); }
int pal_thread_detach(void *thread) { return scePthreadDetach(thread); }
void pal_thread_exit(void *value) { scePthreadExit(value); }
void *pal_thread_self(void) { return scePthreadSelf(); }
void pal_thread_yield(void) { scePthreadYield(); }

// ---------------------------------------------------------------------------
// Mutex and condition variable.
// ---------------------------------------------------------------------------

int pal_mutex_init(void **m) { return scePthreadMutexInit(m, 0, "sharpprospero"); }
int pal_mutex_destroy(void **m) { return scePthreadMutexDestroy(m); }
int pal_mutex_lock(void **m) { return scePthreadMutexLock(m); }
int pal_mutex_trylock(void **m) { return scePthreadMutexTrylock(m); }
int pal_mutex_unlock(void **m) { return scePthreadMutexUnlock(m); }

int pal_cond_init(void **c) { return scePthreadCondInit(c, 0, "sharpprospero"); }
int pal_cond_destroy(void **c) { return scePthreadCondDestroy(c); }
int pal_cond_wait(void **c, void **m) { return scePthreadCondWait(c, m); }
int pal_cond_signal(void **c) { return scePthreadCondSignal(c); }
int pal_cond_broadcast(void **c) { return scePthreadCondBroadcast(c); }

// ---------------------------------------------------------------------------
// Thread-local storage.
// ---------------------------------------------------------------------------

int pal_tls_alloc(unsigned int *key, void (*destructor)(void *)) { return scePthreadKeyCreate(key, destructor); }
int pal_tls_free(unsigned int key) { return scePthreadKeyDelete(key); }
int pal_tls_set(unsigned int key, void *value) { return scePthreadSetspecific(key, value); }
void *pal_tls_get(unsigned int key) { return scePthreadGetspecific(key); }

// ---------------------------------------------------------------------------
// Time and sleep.
// ---------------------------------------------------------------------------

// Nanoseconds on a monotonic clock, taken from the process-time counter (microseconds).
uint64_t pal_monotonic_ns(void)
{
    return (uint64_t)sceKernelGetProcessTime() * 1000ull;
}

void pal_sleep_ns(uint64_t nanoseconds)
{
    uint64_t micros = nanoseconds / 1000ull;
    if (micros == 0 && nanoseconds != 0)
        micros = 1;
    while (micros > 0)
    {
        unsigned int chunk = micros > 0xFFFFFFFFu ? 0xFFFFFFFFu : (unsigned int)micros;
        sceKernelUsleep(chunk);
        micros -= chunk;
    }
}
