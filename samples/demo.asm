; demo.asm - eyeball test for the MASM (ml64) syntax colouring extension
; Build:  ml64 /c /Fo demo.obj demo.asm

COMMENT !
    This whole block is a MASM COMMENT directive.
    Everything up to the next exclamation mark is comment text,
    including keywords like mov, rax and PROC.
!

OPTION CASEMAP:NONE

PUBLIC  AddNumbers

MAXCOUNT    EQU     10h                 ; hex literal
Greeting    db      "Hello, ml64!", 0Ah, 0   ; string + numbers
Mask64      dq      0FFFFFFFFFFFFFFFFh
Pi          REAL8   3.14159265358979
Flags       dd      1010b, 777o, 0x1F, 42

            .code

; ---------------------------------------------------------------------------
; int64 AddNumbers(int64 *values, unsigned count)
; ---------------------------------------------------------------------------
AddNumbers  PROC
            xor     rax, rax
            test    rdx, rdx
            jz      done
            xor     r10, r10
next:
            add     rax, qword ptr [rcx + r10*8]
            inc     r10
            cmp     r10, rdx
            jb      next
done:
            ret
AddNumbers  ENDP

; SSE example with a line-continuation
            .code
ScaleVec    PROC
            movaps  xmm0, xmmword ptr [rcx]
            mulps   xmm0, \
                    xmmword ptr [rdx]        ; continued statement
            movaps  xmmword ptr [r8], xmm0
            ret
ScaleVec    ENDP

            END
