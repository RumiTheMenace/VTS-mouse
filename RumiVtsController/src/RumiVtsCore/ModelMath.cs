namespace RumiVtsController
{
    internal static class ModelMath
    {
        public static float PositionToPixelsY(float normalizedPosition, int screenHeight)
        {
            return normalizedPosition * (screenHeight / 2.0f);
        }

    }
}
