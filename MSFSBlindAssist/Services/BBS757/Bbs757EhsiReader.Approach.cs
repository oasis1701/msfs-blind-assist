using System.Buffers.Binary;
using System.Text;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>The VOR and ILS picture (plan 28's <see cref="Bbs757EhsiApproach"/>), read in every reading where a row can
/// show it: in VOR and ILS mode the nd's own fields, the PFD's glideslope, the DME and the localizer the selected receiver
/// has; in every mode but PLAN, while the IRS is valid, the two bearing pointers (their VOR station by the EFIS mode,
/// <see cref="TryVorStation"/>). Every offset, length and kind is the layout's. A null
/// object pointer is "none" (no glideslope, no DME, no pointer); one outside the module, a station ident that is not text
/// or a position not finite and in range is torn.</summary>
public sealed partial class Bbs757EhsiReader
{
    /// <summary>An ADF bearing pointer's source, the app's own name for it: the pointer is a symbol, and the screen
    /// prints no word for it.</summary>
    private const string AdfLeftName = "ADF L", AdfRightName = "ADF R";

    /// <summary>The app's own guard on the layout's ident length (the stack buffer's size), not a BlueBird value.</summary>
    private const int MaxIdentLength = 64;

    /// <param name="picture">Whether to read the VOR and ILS picture (VOR and ILS mode, the only ones that draw it); else
    /// its values are the defaults and no row shows them.</param>
    /// <param name="pointers">Whether to read the bearing pointers (every mode but PLAN, while the IRS is valid).</param>
    private bool TryApproach(uint nd, uint source, Bbs757EhsiApproach a, bool picture, bool ilsMode, bool pointers,
        out Bbs757EhsiApproachReading? reading)
    {
        reading = null;
        Bbs757EhsiBearingPointer? pointer1 = null, pointer2 = null;
        if (pointers && (!TryBearingPointer(nd, a, ilsMode, a.RmiLeftHandle, a.NdToVorLeft, a.AdfLeft, a.VorLeftWord, AdfLeftName, out pointer1)
                || !TryBearingPointer(nd, a, ilsMode, a.RmiRightHandle, a.NdToVorRight, a.AdfRight, a.VorRightWord, AdfRightName, out pointer2)))
            return false;
        var words = new Bbs757EhsiApproachWords(a.VorLeftWord, a.VorRightWord, a.IlsWord, a.ToWord, a.FromWord, a.DmeWord,
            a.NoFrequencyWord, a.NoDmeWord, a.LocWord, a.GlideslopeWord);
        if (!picture)
        {
            reading = new Bbs757EhsiApproachReading(0, false, 0, Bbs757EhsiToFrom.None, false, 0, 0, false, 0, false, false, null,
                false, false, false, false, false, 0, false, false, 0, pointer1, pointer2, words, a.DeviationFullScale,
                a.DotsPerUnit, a.GlideslopePin);
            return true;
        }

        if (!F64(nd, a.VorCourse, out double vorCourse) || !F64(nd, a.VorDeviation, out double vorDeviation)
            || !Flag(nd, a.VorDeviationValid, out bool vorBar) || !Int(nd, a.ToFrom, out int toFrom)
            || !F64(nd, a.VorDme, out double vorDme) || !Flag(nd, a.VorDmeValid, out bool vorDmeValid)
            || !F64(nd, a.IlsCourse, out double ilsCourse) || !F64(nd, a.LocalizerDeviation, out double localizer)
            || !Flag(nd, a.LocalizerBar, out bool localizerBar) || !Flag(nd, a.IlsDots, out bool dots)
            || !Flag(nd, a.IlsTest, out bool ilsTest) || !F64(nd, a.IlsFrequency, out double frequency)
            || !Flag(nd, a.LocFlag, out bool locFlag) || !Flag(nd, a.GlideslopeFlag, out bool glideslopeFlag)
            || !Int(nd, a.DisplaySide, out int side))
            return false;

        // The glideslope: the PFD's ILS block, inside the PFD. No PFD: no scale, no pointer.
        bool scale = false, pointer = false, glideslopeTest = false;
        double glideslope = 0;
        if (!Link(nd, a.NdToPfd, out uint pfd)) return false;
        if (pfd != 0)
        {
            if (!Good(pfd) || !At(pfd, a.PfdToIls, out uint ils) || !F64(ils, a.GlideslopeDeviation, out glideslope)
                || !Flag(ils, a.GlideslopeScale, out scale) || !Flag(ils, a.GlideslopePointer, out pointer)
                || !Flag(ils, a.GlideslopeTest, out glideslopeTest))
                return false;
        }

        if (!TryIlsDme(nd, a, out bool ilsDmeValid, out double ilsDme)) return false;

        // The frequency is printed only while one is set and the receiver the instrument source selects has the
        // localizer; otherwise the screen prints the dashes.
        double? shownFrequency = null;
        if (double.IsFinite(frequency) && frequency != a.NoIlsFrequency)
        {
            if (!TryLocalizerReceived(source, a, out bool received)) return false;
            if (received) shownFrequency = frequency;
        }

        var toFromValue = toFrom == a.ToValue ? Bbs757EhsiToFrom.To : toFrom == a.FromValue ? Bbs757EhsiToFrom.From : Bbs757EhsiToFrom.None;
        reading = new Bbs757EhsiApproachReading(
            VorCourse: vorCourse, VorBar: vorBar, VorDeviation: vorDeviation, ToFrom: toFromValue, VorDmeValid: vorDmeValid,
            VorDme: vorDme, IlsCourse: ilsCourse, LocalizerBar: localizerBar, LocalizerDeviation: localizer, IlsDots: dots,
            IlsTest: ilsTest, IlsFrequency: shownFrequency, LocFlag: locFlag, GlideslopeFlag: glideslopeFlag,
            CaptainSide: side == a.CaptainSide, GlideslopeScale: scale, GlideslopePointer: pointer,
            GlideslopeDeviation: glideslope, GlideslopeTest: glideslopeTest, IlsDmeValid: ilsDmeValid, IlsDme: ilsDme,
            Pointer1: pointer1, Pointer2: pointer2, Words: words,
            DeviationFullScale: a.DeviationFullScale, DotsPerUnit: a.DotsPerUnit, GlideslopePin: a.GlideslopePin);
        return true;
    }

    /// <summary>ILS mode's DME, valid as BlueBird's draw tests it: the DME not failed and powered, its station an ILS's
    /// (the layout's kind), the distance finite and not negative. No DME or no station: not valid.</summary>
    private bool TryIlsDme(uint nd, Bbs757EhsiApproach a, out bool valid, out double distance)
    {
        valid = false;
        distance = 0;
        if (!Link(nd, a.NdToDme, out uint dme)) return false;
        if (dme == 0) return true;
        if (!Good(dme) || !Flag(dme, a.DmePowered, out bool powered) || !Flag(dme, a.DmeFailed, out bool failed)
            || !Link(dme, a.DmeStation, out uint station) || !F64(dme, a.DmeDistance, out distance))
            return false;
        if (station == 0) return true;
        if (!Good(station) || !U8(station, a.StationKind, out byte kind)) return false;
        valid = powered && !failed && kind == a.IlsStationKind && double.IsFinite(distance) && distance >= 0;
        return true;
    }

    /// <summary>Whether the receiver the instrument source selects has the localizer: <c>[receiver + ReceiverToSignal] +
    /// SignalBlock</c>, then the received byte. No receiver or no signal: not received.</summary>
    private bool TryLocalizerReceived(uint source, Bbs757EhsiApproach a, out bool received)
    {
        received = false;
        if (!Link(source, a.SourceToIlsReceiver, out uint receiver)) return false;
        if (receiver == 0) return true;
        if (!Good(receiver) || !Link(receiver, a.ReceiverToSignal, out uint signal)) return false;
        if (signal == 0) return true;
        return Good(signal) && At(signal, a.SignalBlock, out uint block) && Flag(block, a.LocalizerReceived, out received);
    }

    /// <summary>One bearing pointer, by its selector's L:var (the cached value in its handle): on VOR the VOR panel's
    /// station (<see cref="TryVorStation"/>; none without one), on ADF the ADF, placed only while the layout gives its
    /// receivers' positions (none without a receiver then); any other value draws none.</summary>
    private bool TryBearingPointer(uint nd, Bbs757EhsiApproach a, bool ilsMode, uint handle, int ndToVor, int adfReceiver,
        string vorWord, string adfName, out Bbs757EhsiBearingPointer? pointer)
    {
        pointer = null;
        if (!F64(handle, a.HandleValue, out double selector)) return false;
        if (selector == a.RmiVor)
        {
            if (!Link(nd, ndToVor, out uint panel)) return false;
            if (panel == 0) return true;
            if (!Good(panel) || !TryVorStation(panel, a, ilsMode, out uint station)) return false;
            if (station == 0) return true;
            if (!TryIdent(station, a.StationIdent, a.StationIdentLength, out string ident)
                || !Position(station, a.StationLatitude, a.StationLongitude, out double lat, out double lon))
                return false;
            pointer = new Bbs757EhsiBearingPointer(vorWord, ident, lat, lon);
            return true;
        }
        if (selector != a.RmiAdf) return true;
        if (!a.AdfPositions)
        {
            pointer = new Bbs757EhsiBearingPointer(adfName, null, null, null);
            return true;
        }
        // The ADF panel's receiver: its station's position, the longitude then the latitude, as every position the
        // layout names is stored.
        if (!Link(nd, a.NdToAdf, out uint adf)) return false;
        if (adf == 0) return true;
        if (!Good(adf) || !Link(adf, adfReceiver, out uint receiver)) return false;
        if (receiver == 0) return true;
        if (!Good(receiver) || !Position(receiver, a.AdfLatitude, a.AdfLongitude, out double adfLat, out double adfLon))
            return false;
        pointer = new Bbs757EhsiBearingPointer(adfName, null, adfLat, adfLon);
        return true;
    }

    /// <summary>
    /// The station a VOR panel's bearing pointer points at, as BlueBird's draw picks it: in ILS mode the panel's ILS-mode
    /// station, and only while that station's frequency equals the panel's manual one (whole × 100 + fraction); in the
    /// other modes the panel's station. In every mode only a station whose kind is one of the layout's VOR kinds; else 0
    /// (no pointer). A station outside the module is torn. Two conditions BlueBird also tests are not modeled: the station
    /// in reception (f47465), and the VOR panel's electrical power (f36730, tested in BlueBird's station getter: without
    /// power the getter returns no station while the panel's field keeps it). So a station out of range, or on an
    /// unpowered panel, still reads as pointed at.
    /// </summary>
    private bool TryVorStation(uint panel, Bbs757EhsiApproach a, bool ilsMode, out uint station)
    {
        if (!Link(panel, ilsMode ? a.PanelIlsStation : a.PanelStation, out station)) return false;
        if (station == 0) return true;
        if (!Good(station) || !U8(station, a.StationKind, out byte kind)) return false;
        if (ilsMode)
        {
            if (!U16(station, a.StationFrequency, out ushort frequency) || !Int(panel, a.PanelManualWhole, out int whole)
                || !Int(panel, a.PanelManualFraction, out int fraction))
                return false;
            if (frequency != (long)whole * 100 + fraction) station = 0;
        }
        if (!a.VorKinds.Contains(kind)) station = 0;
        return true;
    }

    /// <summary>A 16-bit value as stored, little-endian.</summary>
    private bool U16(uint b, int offset, out ushort v)
    {
        v = 0;
        Span<byte> bytes = stackalloc byte[2];
        if (!At(b, offset, out uint a) || !_memory.TryRead(a, bytes)) return false;
        v = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        return true;
    }

    /// <summary>A navaid record's ident: up to <paramref name="length"/> (the layout's) UTF-32 code points, ended by the
    /// first 0; each a Unicode scalar value. False when torn, or for a length outside 1 to <see cref="MaxIdentLength"/>.</summary>
    private bool TryIdent(uint station, int offset, int length, out string ident)
    {
        ident = "";
        if (length is < 1 or > MaxIdentLength) return false;
        Span<byte> bytes = stackalloc byte[length * Word];
        if (!At(station, offset, out uint at) || !_memory.TryRead(at, bytes)) return false;
        var sb = new StringBuilder(length);
        Span<char> utf16 = stackalloc char[2];
        for (int i = 0; i < length; i++)
        {
            uint codePoint = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * Word)..]);
            if (codePoint == 0) break;
            if (!Rune.TryCreate(codePoint, out Rune r)) return false;
            sb.Append(utf16[..r.EncodeToUtf16(utf16)]);
        }
        ident = sb.ToString();
        return true;
    }
}
