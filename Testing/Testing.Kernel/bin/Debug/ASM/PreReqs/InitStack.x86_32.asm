; BEGIN - Init stack
mov dword ESP, Kernel_Stack ; Set the stack pointer to point at our pre-allocated block of memory
; END - Init stack