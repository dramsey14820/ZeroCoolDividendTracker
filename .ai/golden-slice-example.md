# Golden Vertical Slice Example (.NET 10+ LTS)

> Pass via `.\.ai\run-stage.ps1 -IncludeGoldenExample` on early slices so `qwen2.5-coder:7b` follows exact project conventions on the first try.

## 1. Contract & Service Interface (`src/MyApp.Shared.UI/Features/Notes/ListNotes/INotesService.cs`)
```csharp
namespace MyApp.Shared.UI.Features.Notes.ListNotes;

public sealed record NoteSummary(Guid Id, string Title, DateTimeOffset CreatedUtc);

public interface INotesService
{
    Task<IReadOnlyList<NoteSummary>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
}
```

## 2. Shared Razor Component (`src/MyApp.Shared.UI/Features/Notes/ListNotes/NotesList.razor`)
```razor
@page "/notes"
@inject INotesService NotesService

<section class="notes-grid" aria-label="Recent Notes">
    @foreach (var note in _notes)
    {
        <article class="note-card">
            <h3>@note.Title</h3>
            <time>@note.CreatedUtc.ToLocalTime().ToString("g")</time>
        </article>
    }
</section>

@code {
    private IReadOnlyList<NoteSummary> _notes = [];

    protected override async Task OnInitializedAsync()
    {
        _notes = await NotesService.GetRecentAsync(20, CancellationToken.None);
    }
}
```

## 3. Isolated CSS Grid & Flexbox (`src/MyApp.Shared.UI/Features/Notes/ListNotes/NotesList.razor.css`)
```css
.notes-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
    gap: 1rem;
}

.note-card {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    padding: 1rem;
}
```

## 4. bUnit + xUnit + FluentAssertions + NSubstitute Test (`tests/MyApp.Tests/Features/Notes/ListNotes/NotesListTests.cs`)
```csharp
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Shared.UI.Features.Notes.ListNotes;
using NSubstitute;
using Xunit;

namespace MyApp.Tests.Features.Notes.ListNotes;

public sealed class NotesListTests : TestContext
{
    [Fact]
    public void NotesList_WhenNotesExist_RendersNoteCards()
    {
        // Arrange
        var service = Substitute.For<INotesService>();
        service.GetRecentAsync(20, Arg.Any<CancellationToken>())
            .Returns([new NoteSummary(Guid.NewGuid(), "First Note", DateTimeOffset.UtcNow)]);
        Services.AddSingleton(service);

        // Act
        var cut = RenderComponent<NotesList>();

        // Assert
        cut.FindAll(".note-card").Should().HaveCount(1);
        cut.Markup.Should().Contain("First Note");
    }
}
```

