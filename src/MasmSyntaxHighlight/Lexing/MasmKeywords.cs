using System;
using System.Collections.Generic;

namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>
    /// Case-insensitive word lists used by <see cref="MasmLexer"/> to classify identifiers.
    /// Add missing instructions / directives here - nothing else needs to change.
    /// </summary>
    internal static class MasmKeywords
    {
        internal static readonly HashSet<string> Registers = New();
        internal static readonly HashSet<string> Mnemonics = New();
        internal static readonly HashSet<string> Directives = New();
        internal static readonly HashSet<string> DataTypes = New();
        internal static readonly HashSet<string> Operators = New();

        /// <summary>Keywords that, when they follow a leading identifier, make that identifier a definition name.</summary>
        internal static readonly HashSet<string> DefinitionFollowers = New();

        /// <summary>Subset of <see cref="DefinitionFollowers"/> that mark the identifier as a procedure / macro name.</summary>
        internal static readonly HashSet<string> ProcDefinitionFollowers = New();

        /// <summary>Followers that mark the identifier as a STRUCT / RECORD / UNION / TYPEDEF name.</summary>
        internal static readonly HashSet<string> TypeDefinitionFollowers = New();

        /// <summary>Followers that mark the identifier as a constant name (EQU / = / TEXTEQU / string equates).</summary>
        internal static readonly HashSet<string> ConstantDefinitionFollowers = New();

        /// <summary>Followers that mark the identifier as a data variable name (db..dq, BYTE..REAL10).</summary>
        internal static readonly HashSet<string> DataDefinitionFollowers = New();

        private static HashSet<string> New() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static MasmKeywords()
        {
            Add(Registers, RegisterList);
            for (int i = 0; i <= 31; i++)
            {
                Registers.Add("xmm" + i);
                Registers.Add("ymm" + i);
                Registers.Add("zmm" + i);
            }
            for (int i = 0; i <= 7; i++)
            {
                Registers.Add("mm" + i);
                Registers.Add("st" + i);
                Registers.Add("k" + i);
                Registers.Add("bnd" + i);
                Registers.Add("tmm" + i);
                Registers.Add("dr" + i);
                Registers.Add("cr" + i);
            }
            for (int i = 8; i <= 15; i++)
            {
                Registers.Add("r" + i);
                Registers.Add("r" + i + "d");
                Registers.Add("r" + i + "w");
                Registers.Add("r" + i + "b");
            }

            Add(Mnemonics, MnemonicList);
            Add(Directives, DirectiveList);
            Add(DataTypes, DataTypeList);
            Add(Operators, OperatorList);
            Add(DefinitionFollowers, DefinitionFollowerList);
            // 'endp' is intentionally absent: the name on an ENDP line is resolved from the
            // matching PROC by MasmSymbols, which keeps the closing line consistent.
            Add(ProcDefinitionFollowers, "proc proto macro");
            Add(TypeDefinitionFollowers, "struc struct record union typedef");
            Add(ConstantDefinitionFollowers, "= equ textequ catstr substr sizestr instr");
            Add(DataDefinitionFollowers,
                "db dw dd df dt dp dq " +
                "byte sbyte word sword dword sdword fword qword sqword tbyte oword " +
                "mmword xmmword ymmword zmmword real4 real8 real10");
        }

        private static void Add(HashSet<string> set, string words)
        {
            foreach (var w in words.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries))
                set.Add(w);
        }

        private static readonly char[] SplitChars = { ' ', '\t', '\r', '\n' };

        private const string RegisterList = @"
rax rbx rcx rdx rsi rdi rbp rsp rip
eax ebx ecx edx esi edi ebp esp eip
ax bx cx dx si di bp sp ip
al bl cl dl ah bh ch dh sil dil bpl spl
cs ds es fs gs ss
st mm
mxcsr gdtr idtr ldtr tr flags eflags rflags
";

        private const string MnemonicList = @"
aaa aad aam aas adc adcx add adox and andn arpl bextr blsi blsmsk blsr bound bsf bsr
bswap bt btc btr bts bzhi call cbw cdq cdqe clac clc cld cldemote clflush clflushopt cli
clts clwb cmc cmova cmovae cmovb cmovbe cmovc cmove cmovg cmovge cmovl cmovle cmovna cmovnae
cmovnb cmovnbe cmovnc cmovne cmovng cmovnge cmovnl cmovnle cmovno cmovnp cmovns cmovnz cmovo
cmovp cmovpe cmovpo cmovs cmovz cmp cmps cmpsb cmpsd cmpsq cmpsw cmpxchg cmpxchg16b cmpxchg8b
cpuid cqo crc32 cwd cwde daa das dec div endbr32 endbr64 enter hlt idiv imul in inc ins insb
insd insw int int1 int3 into invd invlpg invpcid iret iretd iretq
ja jae jb jbe jc jcxz je jecxz jg jge jl jle jmp jna jnae jnb jnbe jnc jne jng jnge jnl jnle
jno jnp jns jnz jo jp jpe jpo jrcxz js jz
lahf lar lds lea leave les lfence lfs lgdt lgs lidt lldt lmsw lock lods lodsb lodsd lodsq
lodsw loop loope loopne loopnz loopz lsl lss ltr lzcnt
mfence mov movbe movd movdir64b movdiri movdq2q movdqa movdqu movnti movntq movq movq2dq
movs movsb movsd movsq movsw movsx movsxd movzx mul mulx mwait
neg nop not or out outs outsb outsd outsw
pause pdep pext popcnt prefetchnta prefetcht0 prefetcht1 prefetcht2 prefetchw
pop popa popad popf popfd popfq push pusha pushad pushf pushfd pushfq
rcl rcr rdfsbase rdgsbase rdmsr rdpid rdpkru rdpmc rdrand rdseed rdtsc rdtscp
rep repe repne repnz repz ret retf retn rol ror rorx rsm
sahf sal sar sarx sbb scas scasb scasd scasq scasw
seta setae setb setbe setc sete setg setge setl setle setna setnae setnb setnbe setnc setne
setng setnge setnl setnle setno setnp setns setnz seto setp setpe setpo sets setz
serialize sfence sgdt shl shld shlx shr shrd shrx sidt sldt smsw stac stc std sti
stos stosb stosd stosq stosw str sub swapgs syscall sysenter sysexit sysret
test tpause tzcnt ud0 ud1 ud2 umonitor umwait
verr verw wait wbinvd wrfsbase wrgsbase wrmsr wrpkru
xabort xacquire xadd xbegin xchg xend xgetbv xlat xlatb xrelease xrstor xsave xsaveopt
xsetbv xtest xadd xor
f2xm1 fabs fadd faddp fbld fbstp fchs fclex fcmovb fcmovbe fcmove fcmovnb fcmovnbe fcmovne
fcmovnu fcmovu fcom fcomi fcomip fcomp fcompp fcos fdecstp fdiv fdivp fdivr fdivrp ffree
fiadd ficom ficomp fidiv fidivr fild fimul fincstp finit fist fistp fisttp fisub fisubr fld
fld1 fldcw fldenv fldl2e fldl2t fldlg2 fldln2 fldpi fldz fmul fmulp fnclex fninit fnop fnsave
fnstcw fnstenv fnstsw fpatan fprem fprem1 fptan frndint frstor fsave fscale fsin fsincos fsqrt
fst fstcw fstenv fstp fstsw fsub fsubp fsubr fsubrp ftst fucom fucomi fucomip fucomp fucompp
fwait fxam fxch fxrstor fxsave fxtract fyl2x fyl2xp1 emms
addpd addps addsd addss addsubpd addsubps aesdec aesdeclast aesenc aesenclast aesimc
aeskeygenassist andnpd andnps andpd andps blendpd blendps blendvpd blendvps
cmppd cmpps cmpsd cmpss comisd comiss
cvtdq2pd cvtdq2ps cvtpd2dq cvtpd2pi cvtpd2ps cvtpi2pd cvtpi2ps cvtps2dq cvtps2pd cvtps2pi
cvtsd2si cvtsd2ss cvtsi2sd cvtsi2ss cvtss2sd cvtss2si cvttpd2dq cvttpd2pi cvttps2dq cvttps2pi
cvttsd2si cvttss2si divpd divps divsd divss dppd dpps extractps
haddpd haddps hsubpd hsubps insertps lddqu ldmxcsr maskmovdqu maskmovq
maxpd maxps maxsd maxss minpd minps minsd minss movapd movaps movddup movdqa movdqu movhlps
movhpd movhps movlhps movlpd movlps movmskpd movmskps movntdq movntdqa movntpd movntps movsd
movshdup movsldup movss movupd movups mpsadbw mulpd mulps mulsd mulss orpd orps
pabsb pabsd pabsw packssdw packsswb packusdw packuswb paddb paddd paddq paddsb paddsw paddusb
paddusw paddw palignr pand pandn pavgb pavgw pblendvb pblendw pclmulqdq pcmpeqb pcmpeqd
pcmpeqq pcmpeqw pcmpestri pcmpestrm pcmpgtb pcmpgtd pcmpgtq pcmpgtw pcmpistri pcmpistrm
pextrb pextrd pextrq pextrw phaddd phaddsw phaddw phminposuw phsubd phsubsw phsubw pinsrb
pinsrd pinsrq pinsrw pmaddubsw pmaddwd pmaxsb pmaxsd pmaxsw pmaxub pmaxud pmaxuw pminsb pminsd
pminsw pminub pminud pminuw pmovmskb pmovsxbd pmovsxbq pmovsxbw pmovsxdq pmovsxwd pmovsxwq
pmovzxbd pmovzxbq pmovzxbw pmovzxdq pmovzxwd pmovzxwq pmuldq pmulhrsw pmulhuw pmulhw pmulld
pmullw pmuludq por psadbw pshufb pshufd pshufhw pshuflw pshufw psignb psignd psignw pslld
pslldq psllq psllw psrad psraw psrld psrldq psrlq psrlw psubb psubd psubq psubsb psubsw psubusb
psubusw psubw ptest punpckhbw punpckhdq punpckhqdq punpckhwd punpcklbw punpckldq punpcklqdq
punpcklwd pxor rcpps rcpss roundpd roundps roundsd roundss rsqrtps rsqrtss
shufpd shufps sqrtpd sqrtps sqrtsd sqrtss stmxcsr subpd subps subsd subss
ucomisd ucomiss unpckhpd unpckhps unpcklpd unpcklps xorpd xorps
vaddpd vaddps vaddsd vaddss vandpd vandps vandnpd vandnps vbroadcastf128 vbroadcastsd
vbroadcastss vblendpd vblendps vblendvpd vblendvps vcmppd vcmpps vcmpsd vcmpss vcomisd vcomiss
vcvtdq2ps vcvtps2dq vcvtsd2si vcvtsi2sd vcvtsi2ss vcvtss2si vcvttps2dq vcvttsd2si vcvttss2si
vdivpd vdivps vdivsd vdivss vextractf128 vextracti128 vfmadd132pd vfmadd132ps vfmadd132sd
vfmadd132ss vfmadd213pd vfmadd213ps vfmadd213sd vfmadd213ss vfmadd231pd vfmadd231ps vfmadd231sd
vfmadd231ss vfmsub132ps vfmsub213ps vfmsub231ps vfnmadd213ss vfnmadd231ss vgatherdpd vgatherdps
vinsertf128 vinserti128 vmaxpd vmaxps vminpd vminps vmovapd vmovaps vmovd vmovdqa vmovdqu
vmovhlps vmovhps vmovlhps vmovlps vmovmskpd vmovmskps vmovntdq vmovq vmovsd vmovss vmovupd
vmovups vmulpd vmulps vmulsd vmulss vorpd vorps vpaddb vpaddd vpaddq vpaddw vpand vpandn
vpblendd vpblendvb vpblendw vpbroadcastb vpbroadcastd vpbroadcastq vpbroadcastw vpcmpeqb
vpcmpeqd vpcmpeqq vpcmpeqw vpcmpgtb vpcmpgtd vpcmpgtq vpcmpgtw vperm2f128 vperm2i128 vpermd
vpermq vpermps vpermpd vpextrb vpextrd vpextrq vpextrw vpgatherdd vpgatherdq vpinsrb vpinsrd
vpinsrq vpinsrw vpmaddwd vpmaxsd vpmaxud vpminsd vpminud vpmovmskb vpmulld vpor vpshufb vpshufd
vpshufhw vpshuflw vpslld vpslldq vpsllq vpsllw vpsrad vpsraw vpsrld vpsrldq vpsrlq vpsrlw
vpsubb vpsubd vpsubq vpsubw vpternlogd vpternlogq vptest vpxor vshufpd vshufps vsqrtpd vsqrtps
vsqrtsd vsqrtss vsubpd vsubps vsubsd vsubss vucomisd vucomiss vunpckhpd vunpckhps vunpcklpd
vunpcklps vxorpd vxorps vzeroall vzeroupper
kaddb kaddw kandb kandw kandnb kandnw kmovb kmovd kmovq kmovw knotb knotw korb korw kortestb
kortestw kshiftlw kshiftrw kunpckbw kxnorw kxorb kxorw
ldtilecfg sttilecfg tdpbf16ps tdpbssd tdpbsud tdpbusd tdpbuud tileloadd tileloaddt1 tilerelease
tilestored tilezero
vmcall vmclear vmfunc vmlaunch vmptrld vmptrst vmread vmresume vmwrite vmxoff vmxon
";

        private const string DirectiveList = @"
proc endp macro endm exitm purge local
struc struct ends union record typedef proto invoke
extern externdef extrn public comm
include includelib end option name title subtitle subttl page
segment group assume align even org
equ textequ label
if ife ifb ifnb ifdef ifndef ifdif ifdifi ifidn ifidni if1 if2 elseif elseifb elseifnb
elseifdef elseifndef elseifdif elseifdifi elseifidn elseifidni elseife else endif
for forc rept repeat irp irpc while goto
catstr substr sizestr instr
echo pushcontext popcontext
.model .code .data .data? .const .stack .fardata .fardata? .dosseg .stack
.286 .286c .286p .287 .386 .386c .386p .387 .486 .486p .586 .586p .686 .686p .k3d .mmx .xmm
.list .nolist .listall .listif .nolistif .listmacro .listmacroall .nolistmacro .tfcond .sall
.cref .nocref .xcref .sfcond .lfcond .seq .alpha .radix .type
.startup .exit .fpo .safeseh
.if .else .elseif .endif .while .endw .repeat .until .untilcxz .break .continue
.allocstack .endprolog .pushframe .pushreg .savereg .savexmm128 .setframe
.err .err1 .err2 .errb .errnb .errdef .errndef .errdif .errdifi .erridn .erridni .erre .errnz
.errblk .errmsg
";

        private const string DataTypeList = @"
db dw dd dq df dt dp
byte sbyte word sword dword sdword fword qword sqword tbyte oword
mmword xmmword ymmword zmmword
real4 real8 real10
near far near16 near32 far16 far32 ptr abs
";

        private const string OperatorList = @"
offset lengthof sizeof type this addr short seg lroffset imagerel sectionrel
dup mask width opattr
high low highword lowword high32 low32
and or xor not shl shr mod
eq ne lt le gt ge
";

        private const string DefinitionFollowerList = @"
proc macro struc struct union record typedef proto
equ textequ label segment group
catstr substr sizestr instr
db dw dd dq df dt dp
byte sbyte word sword dword sdword fword qword sqword tbyte oword
mmword xmmword ymmword zmmword real4 real8 real10
";
    }
}
