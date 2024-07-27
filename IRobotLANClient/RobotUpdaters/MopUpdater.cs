using System;
using IRobotLANClient.Enums;
using IRobotLANClient.JsonObjects;
using IRobotLANClient.RobotInterfaces;

namespace IRobotLANClient.RobotUpdaters;

public class MopUpdater {
	private readonly IMopClient _mopClient;
	
	internal MopUpdater(IMopClient mopClient) {
		_mopClient = mopClient;
	}

	public void Update(ReportedStateMop state) {
		if (state == null) {
        	return;
        }

        // Robot software version c, 22.29.3 moved these values to the top level status object. Let's support both
        // software versions.
        bool tankPresent, lidClosed;

        if (state.MopReady != null) {
        	tankPresent = state.MopReady.TankPresent;
        	lidClosed = state.MopReady.LidClosed;
        } else {
        	tankPresent = state.TankPresent;
        	lidClosed = !state.LidOpen;
        }

        // (Braava jet) In my experience, tankPresent = false always when tankLevel = 0, so we can probably use either
        // one to determine if it needs filling

        if (!lidClosed) {
	        _mopClient.TankStatus = TankStatus.LidOpen;
        } else {
	        _mopClient.TankStatus = state.TankLvl > 0 && tankPresent ? TankStatus.Ok : TankStatus.Empty;
        }
        
        // Roomba Combo
        _mopClient.TankLevel = state.TankLvl;
        _mopClient.DockTankLevel = state.Dock?.TankLvl;

        switch (state.DetectedPad) {
        	case "invalid":
		        _mopClient.MopPadType = MopPadType.Invalid;
        		break;
        	
        	case "reusableWet":
		        _mopClient.MopPadType = MopPadType.ReusableWet;
        		break;
        	
        	case "reusableDry":
		        _mopClient.MopPadType = MopPadType.ReusableDry;
        		break;
        	
        	case "dispWet":
		        _mopClient.MopPadType = MopPadType.DisposableWet;
        		break;
        	
        	case "dispDry":
		        _mopClient.MopPadType = MopPadType.DisposableDry;
        		break;
        	
        	default:
        		Console.WriteLine($"WARNING: Unknown detectedPad: {state.DetectedPad}");
		        _mopClient.MopPadType = MopPadType.Invalid;
        		break;
        }

        _mopClient.WetMopPadWetness = state.PadWetness?.Disposable ?? state.PadWetness?.Reusable ?? 1;
        _mopClient.WetMopRankOverlap = state.RankOverlap;
	}
}