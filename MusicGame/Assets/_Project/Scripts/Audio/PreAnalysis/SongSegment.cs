[System.Serializable]
public struct SongSegment
{
    public float       startTime;
    public float       endTime;
    public float       averageEnergy;
    public float       maxEnergy;
    public EnergyLevel level;
}

public enum EnergyLevel { Low, Mid, High, Drop }
