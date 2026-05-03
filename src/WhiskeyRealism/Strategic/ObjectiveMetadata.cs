namespace WhiskeyRealism.Strategic
{
    public struct ObjectiveMetadata
    {
        public Theater  Theater;
        public Category Category;
        public float    SupplyReachWeight;
        public float    ForeignRecognitionWeight;
        public float    AttritionWeight;
        public float    GeographicCentroidX;
        public float    GeographicCentroidY;

        public bool IsDerived;

        public static ObjectiveMetadata DefaultDerived(Theater theater, float cx, float cy)
        {
            return new ObjectiveMetadata
            {
                Theater = theater,
                Category = Category.Other,
                SupplyReachWeight        = 0.5f,
                ForeignRecognitionWeight = 0.5f,
                AttritionWeight          = 0.5f,
                GeographicCentroidX      = cx,
                GeographicCentroidY      = cy,
                IsDerived = true
            };
        }
    }
}
