﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using iRacingSdkWrapper;
using iRacingSimulator.Drivers;

namespace iRacingSimulator
{
    public class Sim
    {
        private static Lazy<Sim> _instance = new Lazy<Sim>(() => new Sim());
        public static Sim Instance
        {
            get { return _instance.Value; }
        }

        private TelemetryInfo _telemetry, _previousTelemetry;
        private SessionInfo _sessionInfo, _previousSessionInfo;

        private bool _mustUpdateSessionData, _mustReloadDrivers;

        private TimeDelta _timeDelta;

        private Sim()
        {
            _sdk = new SdkWrapper();
            _drivers = new List<Driver>();

            _sessionData = new SessionData();
            _mustUpdateSessionData = true;

            // Attach events
            _sdk.Connected += SdkOnConnected;
            _sdk.Disconnected += SdkOnDisconnected;
            _sdk.TelemetryUpdated += SdkOnTelemetryUpdated;
            _sdk.SessionInfoUpdated += SdkOnSessionInfoUpdated;
        }

        #region Properties

        private readonly SdkWrapper _sdk;
        public SdkWrapper Sdk { get { return _sdk; } }

        private int? _currentSessionNumber;
        public int? CurrentSessionNumber { get { return _currentSessionNumber; } }

        public TelemetryInfo Telemetry { get { return _telemetry; } }
        public SessionInfo SessionInfo { get { return _sessionInfo; } }

        private SessionData _sessionData;
        public SessionData SessionData { get { return _sessionData; } }

        #endregion

        #region Methods
        
        public void Start(double updateFrequency = 10)
        {
            _sdk.Stop();
            _sdk.TelemetryUpdateFrequency = updateFrequency;
            _sdk.Start();
        }

        public void Stop()
        {
            _sdk.Stop();
        }

        #region Drivers

        private readonly List<Driver> _drivers;
        public List<Driver> Drivers { get { return _drivers; } }

        private bool _isUpdatingDrivers;

        private void UpdateDriverList(SessionInfo info)
        {
            _isUpdatingDrivers = true;
            this.GetDrivers(info);
            _isUpdatingDrivers = false;

            this.GetResults(info);
        }

        private void GetDrivers(SessionInfo info)
        {
            if (_mustReloadDrivers)
            {
                _drivers.Clear();
                _mustReloadDrivers = false;
            }

            // Assume max 70 drivers
            for (int id = 0; id < 70; id++)
            {
                // Find existing driver in list
                var driver = _drivers.SingleOrDefault(d => d.Id == id);
                if (driver == null)
                {
                    driver = Driver.FromSessionInfo(info, id);

                    // If no driver found, end of list reached
                    if (driver == null) break;

                    // Add to list
                    _drivers.Add(driver);
                }
                else
                {
                    // Update and check if driver swap occurred
                    var oldId = driver.CustId;
                    var oldName = driver.Name;
                    driver.ParseDynamicSessionInfo(info);

                    if (oldId != driver.CustId)
                    {
                        var e = new DriverSwapEventArgs(oldId, driver.Id, oldName, driver.Name, driver, _telemetry.SessionTime.Value);
                        this.OnDriverSwap(e);
                    }
                }
            }
        }
        
        private void GetResults(SessionInfo info)
        {
            // If currently updating list, or no session yet, then no need to update result info 
            if (_isUpdatingDrivers) return;
            if (_currentSessionNumber == null) return;

            this.GetQualyResults(info);
            this.GetRaceResults(info);
        }

        private void GetQualyResults(SessionInfo info)
        {
            // TODO: stop if qualy is finished
            var query =
                info["QualifyResultsInfo"]["Results"];

            for (int position = 0; position < _drivers.Count; position++)
            {
                var positionQuery = query["Position", position];

                string idValue;
                if (!positionQuery["CarIdx"].TryGetValue(out idValue))
                {
                    // Driver not found
                    continue;
                }

                // Find driver and update results
                int id = int.Parse(idValue);

                var driver = _drivers.SingleOrDefault(d => d.Id == id);
                if (driver != null)
                {
                    driver.UpdateQualyResultsInfo(positionQuery, position);
                }
            }
        }

        private void GetRaceResults(SessionInfo info)
        {
            var query =
                info["SessionInfo"]["Sessions"]["SessionNum", _currentSessionNumber]["ResultsPositions"];

            for (int position = 1; position <= _drivers.Count; position++)
            {
                var positionQuery = query["Position", position];

                string idValue;
                if (!positionQuery["CarIdx"].TryGetValue(out idValue))
                {
                    // Driver not found
                    continue;
                }

                // Find driver and update results
                int id = int.Parse(idValue);

                var driver = _drivers.SingleOrDefault(d => d.Id == id);
                if (driver != null)
                {
                    var previousPosition = driver.Results.Current.ClassPosition;

                    driver.UpdateResultsInfo(_currentSessionNumber.Value, positionQuery, position);

                    if (_telemetry != null)
                    {
                        // Check for new leader
                        if (previousPosition > 1 && driver.Results.Current.ClassPosition == 1)
                        {
                            var e = new LeaderChangeEventArgs(driver, _telemetry.SessionTime.Value);
                            this.OnLeaderChange(e);
                        }

                        // Check for new best lap
                        var bestlap = _sessionData.UpdateFastestLap(driver.CurrentResults.FastestTime, driver);
                        if (bestlap != null)
                        {
                            var e = new FastLapEventArgs(driver, bestlap, _telemetry.SessionTime.Value);
                            this.OnFastLap(e);
                        }
                    }
                }
            }
        }

        private void ResetSession()
        {
            // Need to re-load all drivers when session info updates
            _mustReloadDrivers = true;
        }

        private void UpdateDriverTelemetry(TelemetryInfo info)
        {
            // If currently updating list, no need to update telemetry info 
            if (_isUpdatingDrivers) return;

            foreach (var driver in _drivers)
            {
                driver.UpdateLiveInfo(info);
                driver.Live.CalculateSpeed(_previousTelemetry, _telemetry, _sessionData.Track.Length);
            }

            this.CalculateLivePositions();
            this.UpdateTimeDelta();
        }

        private void CalculateLivePositions()
        {
            Driver leader = null;

            if (this.SessionData.EventType == "Race")
            {
                // Determine live position from lapdistance

                int pos = 1;
                foreach (var driver in _drivers.OrderByDescending(d => d.Live.TotalLapDistance))
                {
                    if (pos == 1) leader = driver;
                    driver.Live.Position = pos;
                    pos++;
                }

                // Determine live class position from live positions and class
                // Group drivers in dictionary with key = classid and value = list of all drivers in that class
                var dict = (from driver in _drivers
                            group driver by driver.Car.CarClassId)
                    .ToDictionary(d => d.Key, d => d.ToList());

                // Set class position
                foreach (var drivers in dict.Values)
                {
                    pos = 1;
                    foreach (var driver in drivers.OrderBy(d => d.Live.Position))
                    {
                        driver.Live.ClassPosition = pos;
                        pos++;
                    }
                }
            }
            else
            {
                // In P or Q, set live position from result position (== best lap according to iRacing)
                foreach (var driver in _drivers.OrderBy(d => d.Results.Current.Position))
                {
                    if (leader == null) leader = driver;
                    driver.Live.Position = driver.Results.Current.Position;
                    driver.Live.ClassPosition = driver.Results.Current.ClassPosition;
                }
            }

            if (leader != null && leader.CurrentResults != null)
                _sessionData.LeaderLap = leader.CurrentResults.LapsComplete + 1;
        }

        private void UpdateTimeDelta()
        {
            if (_timeDelta == null) return;

            // Update the positions of all cars
            _timeDelta.Update(_telemetry.SessionTime.Value, _telemetry.CarIdxLapDistPct.Value);

            // Order drivers by live position
            var drivers = _drivers.OrderBy(d => d.Live.Position).ToList();
            if (drivers.Count > 0)
            {
                // Get leader
                var leader = drivers[0];
                leader.Live.DeltaToLeader = "-";
                leader.Live.DeltaToNext = "-";

                // Loop through drivers
                for (int i = 1; i < drivers.Count; i++)
                {
                    var behind = drivers[i];
                    var ahead = drivers[i - 1];

                    // Lapped?
                    var leaderLapDiff = Math.Abs(leader.Live.TotalLapDistance - behind.Live.TotalLapDistance);
                    var nextLapDiff = Math.Abs(ahead.Live.TotalLapDistance - behind.Live.TotalLapDistance);

                    if (leaderLapDiff < 1)
                    {
                        var leaderDelta = _timeDelta.GetDelta(behind.Id, leader.Id);
                        behind.Live.DeltaToLeader = TimeDelta.DeltaToString(leaderDelta);
                    }
                    else
                    {
                        behind.Live.DeltaToLeader = Math.Floor(leaderLapDiff) + " L";
                    }

                    if (nextLapDiff < 1)
                    {
                        var nextDelta = _timeDelta.GetDelta(behind.Id, ahead.Id);
                        behind.Live.DeltaToNext = TimeDelta.DeltaToString(nextDelta);
                    }
                    else
                    {
                        behind.Live.DeltaToNext = Math.Floor(nextLapDiff) + " L";
                    }
                }
            }
        }

        public void NotifyPitstop(PitstopEventArgs e)
        {
            this.OnPitstop(e);
        }

        #endregion

        #region Events

        private void SdkOnSessionInfoUpdated(object sender, SdkWrapper.SessionInfoUpdatedEventArgs e)
        {
            // Cache previous and current info
            _previousSessionInfo = _sessionInfo;
            _sessionInfo = e.SessionInfo;

            // Stop if we don't have a session number yet
            if (_currentSessionNumber == null) return;

            if (_mustUpdateSessionData)
            {
                _sessionData.Update(e.SessionInfo);
                _timeDelta = new TimeDelta((float)_sessionData.Track.Length * 1000f, 20, 64);
                _mustUpdateSessionData = false;

                this.OnStaticInfoChanged();
            }

            // Update drivers
            this.UpdateDriverList(e.SessionInfo);

            this.OnSessionInfoUpdated(e);
        }

        private void SdkOnTelemetryUpdated(object sender, SdkWrapper.TelemetryUpdatedEventArgs e)
        {
            // Cache previous and current info
            _previousTelemetry = _telemetry;
            _telemetry = e.TelemetryInfo;

            // Check if session changed
            if (_currentSessionNumber == null || (_currentSessionNumber.Value != e.TelemetryInfo.SessionNum.Value))
            {
                _mustUpdateSessionData = true;

                // Session changed, reset session info
                this.ResetSession();
            }

            // Store current session number
            _currentSessionNumber = e.TelemetryInfo.SessionNum.Value;

            // Update session state
            _sessionData.UpdateState(e.TelemetryInfo.SessionState.Value);

            // Update drivers telemetry
            this.UpdateDriverTelemetry(e.TelemetryInfo);

            // Update session data
            this.SessionData.Update(e.TelemetryInfo);

            this.OnTelemetryUpdated(e);
        }

        private void SdkOnDisconnected(object sender, EventArgs e)
        {
            this.OnDisconnected();
        }

        private void SdkOnConnected(object sender, EventArgs e)
        {
            this.OnConnected();
        }

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler StaticInfoChanged;
        public event EventHandler<SdkWrapper.SessionInfoUpdatedEventArgs> SessionInfoUpdated;
        public event EventHandler<SdkWrapper.TelemetryUpdatedEventArgs> TelemetryUpdated;
        public event EventHandler SimulationUpdated;

        public event EventHandler<DriverSwapEventArgs> DriverSwapEvent;
        public event EventHandler<PitstopEventArgs> PitstopEvent;
        public event EventHandler<FastLapEventArgs> FastLapEvent;
        public event EventHandler<LeaderChangeEventArgs> LeaderChangeEvent;

        protected virtual void OnConnected()
        {
            if (this.Connected != null) this.Connected(this, EventArgs.Empty);
        }

        protected virtual void OnDisconnected()
        {
            if (this.Disconnected != null) this.Disconnected(this, EventArgs.Empty);
        }

        protected virtual void OnStaticInfoChanged()
        {
            if (this.StaticInfoChanged != null) this.StaticInfoChanged(this, EventArgs.Empty);
        }

        protected virtual void OnSessionInfoUpdated(SdkWrapper.SessionInfoUpdatedEventArgs e)
        {
            if (this.SessionInfoUpdated != null) this.SessionInfoUpdated(this, e);
        }

        protected virtual void OnTelemetryUpdated(SdkWrapper.TelemetryUpdatedEventArgs e)
        {
            if (this.TelemetryUpdated != null) this.TelemetryUpdated(this, e);
        }

        protected virtual void OnSimulationUpdated()
        {
            if (this.SimulationUpdated != null) this.SimulationUpdated(this, EventArgs.Empty);
        }

        protected virtual void OnDriverSwap(DriverSwapEventArgs e)
        {
            if (this.DriverSwapEvent != null) this.DriverSwapEvent(this, e);
        }

        protected virtual void OnPitstop(PitstopEventArgs e)
        {
            if (this.PitstopEvent != null) this.PitstopEvent(this, e);
        }

        protected virtual void OnFastLap(FastLapEventArgs e)
        {
            if (this.FastLapEvent != null) this.FastLapEvent(this, e);
        }

        protected virtual void OnLeaderChange(LeaderChangeEventArgs e)
        {
            if (this.LeaderChangeEvent != null) this.LeaderChangeEvent(this, e);
        }

        public class DriverSwapEventArgs : RaceEventArgs
        {
            public DriverSwapEventArgs(int prevDriverId, int newDriverId, string prevDriverName, string newDriverName, Driver driver, double time)
                :base (time)
            {
                this.PreviousDriverId = prevDriverId;
                this.NewDriverId = newDriverId;
                this.PreviousDriverName = prevDriverName;
                this.NewDriverName = newDriverName;
                this.NewDriver = driver;
            }

            public int PreviousDriverId { get; set; }
            public int NewDriverId { get; set; }

            public string PreviousDriverName { get; set; }
            public string NewDriverName { get; set; }

            public Driver NewDriver { get; set; }
        }

        public class PitstopEventArgs : RaceEventArgs
        {
            public PitstopEventArgs(PitstopUpdateTypes type, Driver driver, double time)
                : base(time)
            {
                this.Type = type;
                this.Driver = driver;
            }

            public PitstopUpdateTypes Type { get; set; }
            public Driver Driver { get; set; }

            public enum PitstopUpdateTypes
            {
                EnterPitLane = 0,
                EnterPitStall = 1,
                ExitPitStall = 2,
                ExitPitLane = 3
            }
        }

        public class LeaderChangeEventArgs : RaceEventArgs
        {
            public LeaderChangeEventArgs(Driver driver, double time)
                : base(time)
            {
                this.Driver = driver;
            }

            public Driver Driver { get; set; }
        }

        public class FastLapEventArgs : RaceEventArgs
        {
            public FastLapEventArgs(Driver driver, BestLap lap, double time)
                : base(time)
            {
                this.Driver = driver;
                this.Lap = lap;
            }

            public Driver Driver { get; set; }
            public BestLap Lap { get; set; }
        }

        public abstract class RaceEventArgs : EventArgs
        {
            protected RaceEventArgs(double time)
            {
                this.SessionTime = time;
            }

            public double SessionTime { get; set; }
        }

        #endregion

        #endregion

    }
}