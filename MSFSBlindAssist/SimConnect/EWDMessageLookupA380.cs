// Copyright (c) 2025 - Ported from FlyByWire Simulations A380X EcamMessages
// Source: fbw-a380x/src/systems/instruments/src/MsfsAvionicsCommon/EcamMessages/index.ts (EcamMemos)
// Original Copyright (c) 2024-2025 FlyByWire Simulations
// SPDX-License-Identifier: GPL-3.0
//
// Ported ONLY the EcamMemos object literal (the flat code->string map).
// Message strings are preserved byte-for-byte, including all FWC/ANSI color
// escape sequences (\x1b<3m, \x1b4m, \x1bm, etc.).
// ANSI cleaning and color/priority extraction are delegated to the A320
// EWDMessageLookup class to avoid duplication.

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Lookup dictionary for FlyByWire A380X ECAM MEMO messages.
/// Maps numeric codes to formatted MEMO text with ANSI/FWC color escape sequences.
/// Cleaning and priority extraction delegate to <see cref="EWDMessageLookup"/>.
/// </summary>
public static class EWDMessageLookupA380
{
    /// <summary>
    /// Maps 9-digit numeric codes to ECAM MEMO message text with ANSI formatting.
    /// Ported from FBW A380X EcamMessages/index.ts EcamMemos.
    /// </summary>
    private static readonly Dictionary<string, string> Messages = new Dictionary<string, string>
    {
        ["000000001"] = "              \x1b<3mNORMAL",
        ["000001001"] = " \x1b<7m\x1b4mT.O\x1bm",
        ["000001002"] = "   \x1b<5m-SEAT BELTS ....ON",
        ["000001003"] = "   \x1b<3m-SEAT BELTS ON",
        ["000001006"] = "   \x1b<5m-GND SPLRs ....ARM",
        ["000001007"] = "   \x1b<3m-GND SPLRs ARM",
        ["000001008"] = "   \x1b<5m-FLAPS ........T.O",
        ["000001009"] = "   \x1b<3m-FLAPS : T.O",
        ["000001010"] = "   \x1b<5m-AUTO BRK .....RTO",
        ["000001011"] = "   \x1b<3m-AUTO BRK RTO",
        ["000001012"] = "   \x1b<5m-T.O CONFIG ..TEST",
        ["000001013"] = "   \x1b<3m-T.O CONFIG NORM",
        ["000002001"] = " \x1b<7m\x1b4mLDG\x1bm",
        ["000002002"] = "   \x1b<5m-SEAT BELTS ....ON",
        ["000002003"] = "   \x1b<3m-SEAT BELTS ON",
        ["000002006"] = "   \x1b<5m-LDG GEAR ....DOWN",
        ["000002007"] = "   \x1b<3m-LDG GEAR DOWN",
        ["000002008"] = "   \x1b<5m-GND SPLRs ....ARM",
        ["000002009"] = "   \x1b<3m-GND SPLRs ARM",
        ["000002010"] = "   \x1b<5m-FLAPS ........LDG",
        ["000002011"] = "   \x1b<3m-FLAPS : LDG",
        ["000017001"] = "\x1b<3mAPU AVAIL",
        ["000018001"] = "\x1b<3mAPU BLEED",
        ["000029001"] = "\x1b<3mSWITCHG PNL",
        ["210000001"] = "\x1b<3mHI ALT AIRPORT",
        ["220000001"] = "\x1b<2mAP OFF",
        ["220000002"] = "\x1b<4mA/THR OFF",
        ["221000001"] = "\x1b<3mFMS SWTG",
        ["221000002"] = "\x1b<4mDEST EFOB",
        ["240000001"] = "\x1b<3mCOMMERCIAL PART SHED",
        ["241000001"] = "\x1b<4mELEC EXT PWR",
        ["241000002"] = "\x1b<3mELEC EXT PWR",
        ["242000001"] = "\x1b<4mRAT OUT",
        ["242000002"] = "\x1b<3mRAT OUT",
        ["243000001"] = "\x1b<3mREMOTE C/B CTL ON",
        ["230000001"] = "\x1b<3mCAPT ON RMP 3",
        ["230000002"] = "\x1b<3mF/O ON RMP 3",
        ["230000003"] = "\x1b<3mCAPT+F/O ON RMP 3",
        ["230000004"] = "\x1b<3mCABIN READY",
        ["230000005"] = "\x1b<3mCPNY DTLNK NOT AVAIL",
        ["230000006"] = "\x1b<3mGND HF DATALINK OVRD",
        ["230000007"] = "\x1b<3mHF VOICE",
        ["230000008"] = "\x1b<3mPA IN USE",
        ["230000009"] = "\x1b<3mRMP 1+2+3 OFF",
        ["230000010"] = "\x1b<3mRMP 1+3 OFF",
        ["230000011"] = "\x1b<3mRMP 2+3 OFF",
        ["230000012"] = "\x1b<3mRMP 3 OFF",
        ["230000013"] = "\x1b<3mSATCOM ALERT",
        ["230000014"] = "\x1b<3mVHF DTLNK MAN SCAN",
        ["230000015"] = "\x1b<3mVHF VOICE",
        ["271000001"] = "\x1b<3mGND SPLRs ARMED",
        ["280000001"] = "\x1b<3mCROSSFEED OPEN",
        ["280000013"] = "\x1b<4mCROSSFEED OPEN",
        ["280000002"] = "\x1b<3mCOLDFUEL OUTR TK XFR",
        ["280000003"] = "\x1b<3mDEFUEL IN PROGRESS",
        ["280000004"] = "\x1b<3mFWD XFR IN PROGRESS",
        ["280000005"] = "\x1b<3mGND XFR IN PROGRESS",
        ["280000006"] = "\x1b<3mJETTISON IN PROGRESS",
        ["280000007"] = "\x1b<3mOUTR TK XFR IN PROG",
        ["280000008"] = "\x1b<3mOUTR TKS XFRD",
        ["280000009"] = "\x1b<3mREFUEL IN PROGRESS",
        ["280000010"] = "\x1b<3mREFUEL PNL DOOR OPEN",
        ["280000011"] = "\x1b<3mREFUEL PNL DOOR OPEN",
        ["280000012"] = "\x1b<3mTRIM TK XFRD",
        ["290000001"] = "\x1b<3mG ELEC PMP A CTL",
        ["290000002"] = "\x1b<3mG ELEC PMP B CTL",
        ["290000003"] = "\x1b<3mY ELEC PMP A CTL",
        ["290000004"] = "\x1b<3mY ELEC PMP B CTL",
        ["300000001"] = "\x1b<3mENG A-ICE",
        ["300000002"] = "\x1b<3mWING A-ICE",
        ["300000003"] = "\x1b<3mICE NOT DETECTED",
        ["310000001"] = "\x1b<4mMEMO NOT AVAIL",
        ["314000001"] = "\x1b<6mT.O INHIBIT",
        ["314000002"] = "\x1b<6mLDG INHIBIT",
        ["317000001"] = "\x1b<3mCLOCK INT",
        ["320000001"] = "\x1b<4mAUTO BRK OFF",
        ["320000002"] = "\x1b<3mPARK BRK ON",
        ["321000001"] = "\x1b<3mFLT L/G DOWN",
        ["321000002"] = "\x1b<3mL/G GRVTY EXTN",
        ["322000001"] = "\x1b<4mN/W STEER DISC",
        ["322000002"] = "\x1b<3mN/W STEER DISC",
        ["333000001"] = "\x1b<3mSTROBE LT OFF",
        ["335000001"] = "\x1b<3mSEAT BELTS",
        ["335000002"] = "\x1b<3mNO SMOKING",
        ["335000003"] = "\x1b<3mNO MOBILE",
        ["340000001"] = "\x1b<3mTRUE NORTH REF",
        ["340002701"] = "\x1b<3mIR 1 IN ATT ALIGN",
        ["340002702"] = "\x1b<3mIR 2 IN ATT ALIGN",
        ["340002703"] = "\x1b<3mIR 3 IN ATT ALIGN",
        ["340002704"] = "\x1b<3mIR 1+2 IN ATT ALIGN",
        ["340002705"] = "\x1b<3mIR 1+3 IN ATT ALIGN",
        ["340002706"] = "\x1b<3mIR 2+3 IN ATT ALIGN",
        ["340002707"] = "\x1b<3mIR 1+2+3 IN ATT ALIGN",
        ["340003001"] = "\x1b<3mIR IN ALIGN > 7 MN",
        ["340003002"] = "\x1b<4mIR IN ALIGN > 7 MN",
        ["340003003"] = "\x1b<3mIR IN ALIGN 6 MN",
        ["340003004"] = "\x1b<4mIR IN ALIGN 6 MN",
        ["340003005"] = "\x1b<3mIR IN ALIGN 5 MN",
        ["340003006"] = "\x1b<4mIR IN ALIGN 5 MN",
        ["340003007"] = "\x1b<3mIR IN ALIGN 4 MN",
        ["340003008"] = "\x1b<4mIR IN ALIGN 4 MN",
        ["340003101"] = "\x1b<3mIR IN ALIGN 3 MN",
        ["340003102"] = "\x1b<4mIR IN ALIGN 3 MN",
        ["340003103"] = "\x1b<3mIR IN ALIGN 2 MN",
        ["340003104"] = "\x1b<4mIR IN ALIGN 2 MN",
        ["340003105"] = "\x1b<3mIR IN ALIGN 1 MN",
        ["340003106"] = "\x1b<4mIR IN ALIGN 1 MN",
        ["340003107"] = "\x1b<3mIR IN ALIGN",
        ["340003108"] = "\x1b<4mIR IN ALIGN",
        ["340003109"] = "\x1b<3mIR ALIGNED",
        ["340068001"] = "\x1b<3mADIRS SWTG",
        ["341000001"] = "\x1b<3mGPWS OFF",
        ["341000002"] = "\x1b<3mTAWS FLAP MODE OFF",
        ["341000003"] = "\x1b<3mTAWS G/S MODE OFF",
        ["341000004"] = "\x1b<3mTERR SYS OFF",
        ["341000005"] = "\x1b<3mTERR STBY",
        ["342000001"] = "\x1b<4mPRED W/S OFF",
        ["342000002"] = "\x1b<3mPRED W/S OFF",
        ["342000003"] = "\x1b<3mWXR TURB OFF",
        ["342000004"] = "\x1b<3mWXR ON",
        ["342000005"] = "\x1b<4mWXR OFF",
        ["343000001"] = "\x1b<3mTCAS STBY",
        ["343000002"] = "\x1b<3mALT RPTG OFF",
        ["343000003"] = "\x1b<3mXPDR STBY",
        ["350000001"] = "\x1b<3mOXY PAX SYS ON",
        ["350000002"] = "\x1b<4mOXY PAX SYS ON",
        ["460000001"] = "\x1b<3mCOMPANY MSG",
        ["460000002"] = "\x1b<3mCOMPANY MSG:PRNTR",
        ["460000003"] = "\x1b<3mCOMPANY ALERT",
        ["460000004"] = "\x1b<3mCOMPANY ALERT:PRNTR",
        ["460000005"] = "\x1b<3mCALL COMPANY",
        ["709000001"] = "\x1b<3mIGNITION",
    };

    /// <summary>
    /// Looks up an ECAM MEMO message by its numeric code and returns cleaned text.
    /// </summary>
    /// <param name="code">9-digit numeric code</param>
    /// <returns>Cleaned message text without ANSI codes, or empty string if not found</returns>
    public static string GetMessage(long code)
    {
        // Convert to 9-digit string with leading zeros
        string codeString = code.ToString("D9");

        if (Messages.TryGetValue(codeString, out string? rawMessage))
        {
            return EWDMessageLookup.CleanANSICodes(rawMessage);
        }

        return ""; // Return empty string for unknown codes or code 0
    }

    /// <summary>
    /// Looks up an ECAM MEMO message by its numeric code and returns raw text with ANSI codes intact.
    /// </summary>
    /// <param name="code">9-digit numeric code</param>
    /// <returns>Raw message text with ANSI codes, or empty string if not found</returns>
    public static string GetRawMessage(long code)
    {
        // Convert to 9-digit string with leading zeros
        string codeString = code.ToString("D9");

        if (Messages.TryGetValue(codeString, out string? rawMessage))
        {
            return rawMessage;
        }

        return ""; // Return empty string for unknown codes or code 0
    }

    /// <summary>
    /// Extracts color/priority information for a MEMO code by delegating to the A320 lookup.
    /// </summary>
    /// <param name="code">9-digit numeric code</param>
    /// <returns>Color string (Red, Amber, Green, White, Cyan, Gray, or empty)</returns>
    public static string GetMessagePriority(long code)
    {
        return EWDMessageLookup.GetMessagePriority(GetRawMessage(code));
    }
}
