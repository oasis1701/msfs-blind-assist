using System.Runtime.InteropServices;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Batch 1 structure for continuous variable monitoring (variables 0-299).
/// Part of multi-batch system to reduce SimConnect load per request.
/// Contains 300 double fields for batched SimConnect data requests.
///
/// IMPORTANT: Pack = 8 ensures proper 8-byte alignment for doubles, preventing
/// ExecutionEngineException when accessing fields via unsafe pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct GenericBatch1
{
    // Capacity extended to 300 doubles (BATCH_SIZE = 300). Field NAMES are
    // irrelevant — ProcessContinuousBatchImpl reads the struct as a flat double[]
    // by index, and the data definition is filled in add-order; only the total
    // size (>= BATCH_SIZE doubles) matters.
    public double V200; public double V201; public double V202; public double V203; public double V204;
    public double V205; public double V206; public double V207; public double V208; public double V209;
    public double V210; public double V211; public double V212; public double V213; public double V214;
    public double V215; public double V216; public double V217; public double V218; public double V219;
    public double V220; public double V221; public double V222; public double V223; public double V224;
    public double V225; public double V226; public double V227; public double V228; public double V229;
    public double V230; public double V231; public double V232; public double V233; public double V234;
    public double V235; public double V236; public double V237; public double V238; public double V239;
    public double V240; public double V241; public double V242; public double V243; public double V244;
    public double V245; public double V246; public double V247; public double V248; public double V249;
    public double V250; public double V251; public double V252; public double V253; public double V254;
    public double V255; public double V256; public double V257; public double V258; public double V259;
    public double V260; public double V261; public double V262; public double V263; public double V264;
    public double V265; public double V266; public double V267; public double V268; public double V269;
    public double V270; public double V271; public double V272; public double V273; public double V274;
    public double V275; public double V276; public double V277; public double V278; public double V279;
    public double V280; public double V281; public double V282; public double V283; public double V284;
    public double V285; public double V286; public double V287; public double V288; public double V289;
    public double V290; public double V291; public double V292; public double V293; public double V294;
    public double V295; public double V296; public double V297; public double V298; public double V299;
    public double V100; public double V101; public double V102; public double V103; public double V104;
    public double V105; public double V106; public double V107; public double V108; public double V109;
    public double V110; public double V111; public double V112; public double V113; public double V114;
    public double V115; public double V116; public double V117; public double V118; public double V119;
    public double V120; public double V121; public double V122; public double V123; public double V124;
    public double V125; public double V126; public double V127; public double V128; public double V129;
    public double V130; public double V131; public double V132; public double V133; public double V134;
    public double V135; public double V136; public double V137; public double V138; public double V139;
    public double V140; public double V141; public double V142; public double V143; public double V144;
    public double V145; public double V146; public double V147; public double V148; public double V149;
    public double V150; public double V151; public double V152; public double V153; public double V154;
    public double V155; public double V156; public double V157; public double V158; public double V159;
    public double V160; public double V161; public double V162; public double V163; public double V164;
    public double V165; public double V166; public double V167; public double V168; public double V169;
    public double V170; public double V171; public double V172; public double V173; public double V174;
    public double V175; public double V176; public double V177; public double V178; public double V179;
    public double V180; public double V181; public double V182; public double V183; public double V184;
    public double V185; public double V186; public double V187; public double V188; public double V189;
    public double V190; public double V191; public double V192; public double V193; public double V194;
    public double V195; public double V196; public double V197; public double V198; public double V199;
    public double V0; public double V1; public double V2; public double V3; public double V4;
    public double V5; public double V6; public double V7; public double V8; public double V9;
    public double V10; public double V11; public double V12; public double V13; public double V14;
    public double V15; public double V16; public double V17; public double V18; public double V19;
    public double V20; public double V21; public double V22; public double V23; public double V24;
    public double V25; public double V26; public double V27; public double V28; public double V29;
    public double V30; public double V31; public double V32; public double V33; public double V34;
    public double V35; public double V36; public double V37; public double V38; public double V39;
    public double V40; public double V41; public double V42; public double V43; public double V44;
    public double V45; public double V46; public double V47; public double V48; public double V49;
    public double V50; public double V51; public double V52; public double V53; public double V54;
    public double V55; public double V56; public double V57; public double V58; public double V59;
    public double V60; public double V61; public double V62; public double V63; public double V64;
    public double V65; public double V66; public double V67; public double V68; public double V69;
    public double V70; public double V71; public double V72; public double V73; public double V74;
    public double V75; public double V76; public double V77; public double V78; public double V79;
    public double V80; public double V81; public double V82; public double V83; public double V84;
    public double V85; public double V86; public double V87; public double V88; public double V89;
    public double V90; public double V91; public double V92; public double V93; public double V94;
    public double V95; public double V96; public double V97; public double V98; public double V99;
}

/// <summary>
/// Batch 2 structure for continuous variable monitoring (variables 300-599).
/// Part of multi-batch system to reduce SimConnect load per request.
/// Contains 300 double fields for batched SimConnect data requests.
///
/// IMPORTANT: Pack = 8 ensures proper 8-byte alignment for doubles, preventing
/// ExecutionEngineException when accessing fields via unsafe pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct GenericBatch2
{
    // Capacity extended to 300 doubles (BATCH_SIZE = 300) — see GenericBatch1.
    public double V200; public double V201; public double V202; public double V203; public double V204;
    public double V205; public double V206; public double V207; public double V208; public double V209;
    public double V210; public double V211; public double V212; public double V213; public double V214;
    public double V215; public double V216; public double V217; public double V218; public double V219;
    public double V220; public double V221; public double V222; public double V223; public double V224;
    public double V225; public double V226; public double V227; public double V228; public double V229;
    public double V230; public double V231; public double V232; public double V233; public double V234;
    public double V235; public double V236; public double V237; public double V238; public double V239;
    public double V240; public double V241; public double V242; public double V243; public double V244;
    public double V245; public double V246; public double V247; public double V248; public double V249;
    public double V250; public double V251; public double V252; public double V253; public double V254;
    public double V255; public double V256; public double V257; public double V258; public double V259;
    public double V260; public double V261; public double V262; public double V263; public double V264;
    public double V265; public double V266; public double V267; public double V268; public double V269;
    public double V270; public double V271; public double V272; public double V273; public double V274;
    public double V275; public double V276; public double V277; public double V278; public double V279;
    public double V280; public double V281; public double V282; public double V283; public double V284;
    public double V285; public double V286; public double V287; public double V288; public double V289;
    public double V290; public double V291; public double V292; public double V293; public double V294;
    public double V295; public double V296; public double V297; public double V298; public double V299;
    public double V100; public double V101; public double V102; public double V103; public double V104;
    public double V105; public double V106; public double V107; public double V108; public double V109;
    public double V110; public double V111; public double V112; public double V113; public double V114;
    public double V115; public double V116; public double V117; public double V118; public double V119;
    public double V120; public double V121; public double V122; public double V123; public double V124;
    public double V125; public double V126; public double V127; public double V128; public double V129;
    public double V130; public double V131; public double V132; public double V133; public double V134;
    public double V135; public double V136; public double V137; public double V138; public double V139;
    public double V140; public double V141; public double V142; public double V143; public double V144;
    public double V145; public double V146; public double V147; public double V148; public double V149;
    public double V150; public double V151; public double V152; public double V153; public double V154;
    public double V155; public double V156; public double V157; public double V158; public double V159;
    public double V160; public double V161; public double V162; public double V163; public double V164;
    public double V165; public double V166; public double V167; public double V168; public double V169;
    public double V170; public double V171; public double V172; public double V173; public double V174;
    public double V175; public double V176; public double V177; public double V178; public double V179;
    public double V180; public double V181; public double V182; public double V183; public double V184;
    public double V185; public double V186; public double V187; public double V188; public double V189;
    public double V190; public double V191; public double V192; public double V193; public double V194;
    public double V195; public double V196; public double V197; public double V198; public double V199;
    public double V0; public double V1; public double V2; public double V3; public double V4;
    public double V5; public double V6; public double V7; public double V8; public double V9;
    public double V10; public double V11; public double V12; public double V13; public double V14;
    public double V15; public double V16; public double V17; public double V18; public double V19;
    public double V20; public double V21; public double V22; public double V23; public double V24;
    public double V25; public double V26; public double V27; public double V28; public double V29;
    public double V30; public double V31; public double V32; public double V33; public double V34;
    public double V35; public double V36; public double V37; public double V38; public double V39;
    public double V40; public double V41; public double V42; public double V43; public double V44;
    public double V45; public double V46; public double V47; public double V48; public double V49;
    public double V50; public double V51; public double V52; public double V53; public double V54;
    public double V55; public double V56; public double V57; public double V58; public double V59;
    public double V60; public double V61; public double V62; public double V63; public double V64;
    public double V65; public double V66; public double V67; public double V68; public double V69;
    public double V70; public double V71; public double V72; public double V73; public double V74;
    public double V75; public double V76; public double V77; public double V78; public double V79;
    public double V80; public double V81; public double V82; public double V83; public double V84;
    public double V85; public double V86; public double V87; public double V88; public double V89;
    public double V90; public double V91; public double V92; public double V93; public double V94;
    public double V95; public double V96; public double V97; public double V98; public double V99;
}

/// <summary>
/// Batch 3 structure for continuous variable monitoring (variables 600-899).
/// Part of multi-batch system to reduce SimConnect load per request.
/// Contains 300 double fields for batched SimConnect data requests.
///
/// IMPORTANT: Pack = 8 ensures proper 8-byte alignment for doubles, preventing
/// ExecutionEngineException when accessing fields via unsafe pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct GenericBatch3
{
    // Capacity extended to 300 doubles (BATCH_SIZE = 300) — see GenericBatch1.
    public double V200; public double V201; public double V202; public double V203; public double V204;
    public double V205; public double V206; public double V207; public double V208; public double V209;
    public double V210; public double V211; public double V212; public double V213; public double V214;
    public double V215; public double V216; public double V217; public double V218; public double V219;
    public double V220; public double V221; public double V222; public double V223; public double V224;
    public double V225; public double V226; public double V227; public double V228; public double V229;
    public double V230; public double V231; public double V232; public double V233; public double V234;
    public double V235; public double V236; public double V237; public double V238; public double V239;
    public double V240; public double V241; public double V242; public double V243; public double V244;
    public double V245; public double V246; public double V247; public double V248; public double V249;
    public double V250; public double V251; public double V252; public double V253; public double V254;
    public double V255; public double V256; public double V257; public double V258; public double V259;
    public double V260; public double V261; public double V262; public double V263; public double V264;
    public double V265; public double V266; public double V267; public double V268; public double V269;
    public double V270; public double V271; public double V272; public double V273; public double V274;
    public double V275; public double V276; public double V277; public double V278; public double V279;
    public double V280; public double V281; public double V282; public double V283; public double V284;
    public double V285; public double V286; public double V287; public double V288; public double V289;
    public double V290; public double V291; public double V292; public double V293; public double V294;
    public double V295; public double V296; public double V297; public double V298; public double V299;
    public double V100; public double V101; public double V102; public double V103; public double V104;
    public double V105; public double V106; public double V107; public double V108; public double V109;
    public double V110; public double V111; public double V112; public double V113; public double V114;
    public double V115; public double V116; public double V117; public double V118; public double V119;
    public double V120; public double V121; public double V122; public double V123; public double V124;
    public double V125; public double V126; public double V127; public double V128; public double V129;
    public double V130; public double V131; public double V132; public double V133; public double V134;
    public double V135; public double V136; public double V137; public double V138; public double V139;
    public double V140; public double V141; public double V142; public double V143; public double V144;
    public double V145; public double V146; public double V147; public double V148; public double V149;
    public double V150; public double V151; public double V152; public double V153; public double V154;
    public double V155; public double V156; public double V157; public double V158; public double V159;
    public double V160; public double V161; public double V162; public double V163; public double V164;
    public double V165; public double V166; public double V167; public double V168; public double V169;
    public double V170; public double V171; public double V172; public double V173; public double V174;
    public double V175; public double V176; public double V177; public double V178; public double V179;
    public double V180; public double V181; public double V182; public double V183; public double V184;
    public double V185; public double V186; public double V187; public double V188; public double V189;
    public double V190; public double V191; public double V192; public double V193; public double V194;
    public double V195; public double V196; public double V197; public double V198; public double V199;
    public double V0; public double V1; public double V2; public double V3; public double V4;
    public double V5; public double V6; public double V7; public double V8; public double V9;
    public double V10; public double V11; public double V12; public double V13; public double V14;
    public double V15; public double V16; public double V17; public double V18; public double V19;
    public double V20; public double V21; public double V22; public double V23; public double V24;
    public double V25; public double V26; public double V27; public double V28; public double V29;
    public double V30; public double V31; public double V32; public double V33; public double V34;
    public double V35; public double V36; public double V37; public double V38; public double V39;
    public double V40; public double V41; public double V42; public double V43; public double V44;
    public double V45; public double V46; public double V47; public double V48; public double V49;
    public double V50; public double V51; public double V52; public double V53; public double V54;
    public double V55; public double V56; public double V57; public double V58; public double V59;
    public double V60; public double V61; public double V62; public double V63; public double V64;
    public double V65; public double V66; public double V67; public double V68; public double V69;
    public double V70; public double V71; public double V72; public double V73; public double V74;
    public double V75; public double V76; public double V77; public double V78; public double V79;
    public double V80; public double V81; public double V82; public double V83; public double V84;
    public double V85; public double V86; public double V87; public double V88; public double V89;
    public double V90; public double V91; public double V92; public double V93; public double V94;
    public double V95; public double V96; public double V97; public double V98; public double V99;
}

/// <summary>
/// Batch 4 structure for continuous variable monitoring (variables 900-1199).
/// Part of multi-batch system to reduce SimConnect load per request.
/// Contains 300 double fields for batched SimConnect data requests.
///
/// IMPORTANT: Pack = 8 ensures proper 8-byte alignment for doubles, preventing
/// ExecutionEngineException when accessing fields via unsafe pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct GenericBatch4
{
    // Capacity extended to 300 doubles (BATCH_SIZE = 300) — see GenericBatch1.
    public double V200; public double V201; public double V202; public double V203; public double V204;
    public double V205; public double V206; public double V207; public double V208; public double V209;
    public double V210; public double V211; public double V212; public double V213; public double V214;
    public double V215; public double V216; public double V217; public double V218; public double V219;
    public double V220; public double V221; public double V222; public double V223; public double V224;
    public double V225; public double V226; public double V227; public double V228; public double V229;
    public double V230; public double V231; public double V232; public double V233; public double V234;
    public double V235; public double V236; public double V237; public double V238; public double V239;
    public double V240; public double V241; public double V242; public double V243; public double V244;
    public double V245; public double V246; public double V247; public double V248; public double V249;
    public double V250; public double V251; public double V252; public double V253; public double V254;
    public double V255; public double V256; public double V257; public double V258; public double V259;
    public double V260; public double V261; public double V262; public double V263; public double V264;
    public double V265; public double V266; public double V267; public double V268; public double V269;
    public double V270; public double V271; public double V272; public double V273; public double V274;
    public double V275; public double V276; public double V277; public double V278; public double V279;
    public double V280; public double V281; public double V282; public double V283; public double V284;
    public double V285; public double V286; public double V287; public double V288; public double V289;
    public double V290; public double V291; public double V292; public double V293; public double V294;
    public double V295; public double V296; public double V297; public double V298; public double V299;
    public double V100; public double V101; public double V102; public double V103; public double V104;
    public double V105; public double V106; public double V107; public double V108; public double V109;
    public double V110; public double V111; public double V112; public double V113; public double V114;
    public double V115; public double V116; public double V117; public double V118; public double V119;
    public double V120; public double V121; public double V122; public double V123; public double V124;
    public double V125; public double V126; public double V127; public double V128; public double V129;
    public double V130; public double V131; public double V132; public double V133; public double V134;
    public double V135; public double V136; public double V137; public double V138; public double V139;
    public double V140; public double V141; public double V142; public double V143; public double V144;
    public double V145; public double V146; public double V147; public double V148; public double V149;
    public double V150; public double V151; public double V152; public double V153; public double V154;
    public double V155; public double V156; public double V157; public double V158; public double V159;
    public double V160; public double V161; public double V162; public double V163; public double V164;
    public double V165; public double V166; public double V167; public double V168; public double V169;
    public double V170; public double V171; public double V172; public double V173; public double V174;
    public double V175; public double V176; public double V177; public double V178; public double V179;
    public double V180; public double V181; public double V182; public double V183; public double V184;
    public double V185; public double V186; public double V187; public double V188; public double V189;
    public double V190; public double V191; public double V192; public double V193; public double V194;
    public double V195; public double V196; public double V197; public double V198; public double V199;
    public double V0; public double V1; public double V2; public double V3; public double V4;
    public double V5; public double V6; public double V7; public double V8; public double V9;
    public double V10; public double V11; public double V12; public double V13; public double V14;
    public double V15; public double V16; public double V17; public double V18; public double V19;
    public double V20; public double V21; public double V22; public double V23; public double V24;
    public double V25; public double V26; public double V27; public double V28; public double V29;
    public double V30; public double V31; public double V32; public double V33; public double V34;
    public double V35; public double V36; public double V37; public double V38; public double V39;
    public double V40; public double V41; public double V42; public double V43; public double V44;
    public double V45; public double V46; public double V47; public double V48; public double V49;
    public double V50; public double V51; public double V52; public double V53; public double V54;
    public double V55; public double V56; public double V57; public double V58; public double V59;
    public double V60; public double V61; public double V62; public double V63; public double V64;
    public double V65; public double V66; public double V67; public double V68; public double V69;
    public double V70; public double V71; public double V72; public double V73; public double V74;
    public double V75; public double V76; public double V77; public double V78; public double V79;
    public double V80; public double V81; public double V82; public double V83; public double V84;
    public double V85; public double V86; public double V87; public double V88; public double V89;
    public double V90; public double V91; public double V92; public double V93; public double V94;
    public double V95; public double V96; public double V97; public double V98; public double V99;
}

/// <summary>
/// Batch 5 structure for continuous variable monitoring (variables 1200-1499).
/// Part of multi-batch system to reduce SimConnect load per request.
/// Contains 300 double fields for batched SimConnect data requests.
///
/// IMPORTANT: Pack = 8 ensures proper 8-byte alignment for doubles, preventing
/// ExecutionEngineException when accessing fields via unsafe pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct GenericBatch5
{
    // Capacity extended to 300 doubles (BATCH_SIZE = 300) — see GenericBatch1.
    public double V200; public double V201; public double V202; public double V203; public double V204;
    public double V205; public double V206; public double V207; public double V208; public double V209;
    public double V210; public double V211; public double V212; public double V213; public double V214;
    public double V215; public double V216; public double V217; public double V218; public double V219;
    public double V220; public double V221; public double V222; public double V223; public double V224;
    public double V225; public double V226; public double V227; public double V228; public double V229;
    public double V230; public double V231; public double V232; public double V233; public double V234;
    public double V235; public double V236; public double V237; public double V238; public double V239;
    public double V240; public double V241; public double V242; public double V243; public double V244;
    public double V245; public double V246; public double V247; public double V248; public double V249;
    public double V250; public double V251; public double V252; public double V253; public double V254;
    public double V255; public double V256; public double V257; public double V258; public double V259;
    public double V260; public double V261; public double V262; public double V263; public double V264;
    public double V265; public double V266; public double V267; public double V268; public double V269;
    public double V270; public double V271; public double V272; public double V273; public double V274;
    public double V275; public double V276; public double V277; public double V278; public double V279;
    public double V280; public double V281; public double V282; public double V283; public double V284;
    public double V285; public double V286; public double V287; public double V288; public double V289;
    public double V290; public double V291; public double V292; public double V293; public double V294;
    public double V295; public double V296; public double V297; public double V298; public double V299;
    public double V100; public double V101; public double V102; public double V103; public double V104;
    public double V105; public double V106; public double V107; public double V108; public double V109;
    public double V110; public double V111; public double V112; public double V113; public double V114;
    public double V115; public double V116; public double V117; public double V118; public double V119;
    public double V120; public double V121; public double V122; public double V123; public double V124;
    public double V125; public double V126; public double V127; public double V128; public double V129;
    public double V130; public double V131; public double V132; public double V133; public double V134;
    public double V135; public double V136; public double V137; public double V138; public double V139;
    public double V140; public double V141; public double V142; public double V143; public double V144;
    public double V145; public double V146; public double V147; public double V148; public double V149;
    public double V150; public double V151; public double V152; public double V153; public double V154;
    public double V155; public double V156; public double V157; public double V158; public double V159;
    public double V160; public double V161; public double V162; public double V163; public double V164;
    public double V165; public double V166; public double V167; public double V168; public double V169;
    public double V170; public double V171; public double V172; public double V173; public double V174;
    public double V175; public double V176; public double V177; public double V178; public double V179;
    public double V180; public double V181; public double V182; public double V183; public double V184;
    public double V185; public double V186; public double V187; public double V188; public double V189;
    public double V190; public double V191; public double V192; public double V193; public double V194;
    public double V195; public double V196; public double V197; public double V198; public double V199;
    public double V0; public double V1; public double V2; public double V3; public double V4;
    public double V5; public double V6; public double V7; public double V8; public double V9;
    public double V10; public double V11; public double V12; public double V13; public double V14;
    public double V15; public double V16; public double V17; public double V18; public double V19;
    public double V20; public double V21; public double V22; public double V23; public double V24;
    public double V25; public double V26; public double V27; public double V28; public double V29;
    public double V30; public double V31; public double V32; public double V33; public double V34;
    public double V35; public double V36; public double V37; public double V38; public double V39;
    public double V40; public double V41; public double V42; public double V43; public double V44;
    public double V45; public double V46; public double V47; public double V48; public double V49;
    public double V50; public double V51; public double V52; public double V53; public double V54;
    public double V55; public double V56; public double V57; public double V58; public double V59;
    public double V60; public double V61; public double V62; public double V63; public double V64;
    public double V65; public double V66; public double V67; public double V68; public double V69;
    public double V70; public double V71; public double V72; public double V73; public double V74;
    public double V75; public double V76; public double V77; public double V78; public double V79;
    public double V80; public double V81; public double V82; public double V83; public double V84;
    public double V85; public double V86; public double V87; public double V88; public double V89;
    public double V90; public double V91; public double V92; public double V93; public double V94;
    public double V95; public double V96; public double V97; public double V98; public double V99;
}

/// <summary>
/// Panel batch structure for OnRequest panel variable monitoring.
/// Contains 100 double fields (V0-V99) for batched panel variable requests.
/// Used when opening panels to request all panel variables in a single SimConnect call.
///
/// IMPORTANT: Pack = 8 ensures proper 8-byte alignment for doubles, preventing
/// ExecutionEngineException when accessing fields via unsafe pointers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct PanelBatch
{
    public double V0; public double V1; public double V2; public double V3; public double V4;
    public double V5; public double V6; public double V7; public double V8; public double V9;
    public double V10; public double V11; public double V12; public double V13; public double V14;
    public double V15; public double V16; public double V17; public double V18; public double V19;
    public double V20; public double V21; public double V22; public double V23; public double V24;
    public double V25; public double V26; public double V27; public double V28; public double V29;
    public double V30; public double V31; public double V32; public double V33; public double V34;
    public double V35; public double V36; public double V37; public double V38; public double V39;
    public double V40; public double V41; public double V42; public double V43; public double V44;
    public double V45; public double V46; public double V47; public double V48; public double V49;
    public double V50; public double V51; public double V52; public double V53; public double V54;
    public double V55; public double V56; public double V57; public double V58; public double V59;
    public double V60; public double V61; public double V62; public double V63; public double V64;
    public double V65; public double V66; public double V67; public double V68; public double V69;
    public double V70; public double V71; public double V72; public double V73; public double V74;
    public double V75; public double V76; public double V77; public double V78; public double V79;
    public double V80; public double V81; public double V82; public double V83; public double V84;
    public double V85; public double V86; public double V87; public double V88; public double V89;
    public double V90; public double V91; public double V92; public double V93; public double V94;
    public double V95; public double V96; public double V97; public double V98; public double V99;
}
