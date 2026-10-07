using PerceptoX.Application.Matching;

namespace PerceptoX.Application.Tests;

public sealed class LabeledDatasetEvaluatorTests
{
    [Fact]
    public void ReportsPrecisionRecallAndPerVariantRecall()
    {
        SearchIndexSnapshot snapshot = new(1, "test", [
            new(1, 0, 0, 100, 100, 1000),
            new(2, 0, 0, 50, 50, 300),
            new(3, ulong.MaxValue, ulong.MaxValue, 100, 100, 1000),
            new(4, 0, 0, 100, 100, 1000)
        ]);
        LabeledImage[] labels =
        [
            new(1, "a", "original"),
            new(2, "a", "thumbnail"),
            new(3, "a", "crop"),
            new(4, "b", "original")
        ];

        MatchingEvaluation evaluation = new LabeledDatasetEvaluator().Evaluate(snapshot, labels, "original",
            new SimilaritySearchOptions(10, 0, 0));

        Assert.Equal(2, evaluation.Queries);
        Assert.Equal(4, evaluation.Returned);
        Assert.Equal(1, evaluation.RelevantReturned);
        Assert.Equal(2, evaluation.RelevantExpected);
        Assert.Equal(0.25, evaluation.Precision);
        Assert.Equal(0.5, evaluation.Recall);
        Assert.Equal(0, Assert.Single(evaluation.RecallByVariant, item => item.Variant == "crop").Found);
        Assert.Equal(1, Assert.Single(evaluation.RecallByVariant, item => item.Variant == "thumbnail").Found);
    }
}
