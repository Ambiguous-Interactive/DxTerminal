namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using Backend;

    /*
        The log read the frame draws: the buffer's visible window, or the
        lines of it a search keeps, plus the memo that keeps an idle console
        from paying for the read every frame (#222).

        The read used to copy the whole window under the buffer's lock - and,
        with a find query set, rescan every line - on every pass, whether or
        not anything had changed. The memo keys on the four inputs the read
        depends on - the buffer instance, its version, its capacity, and the
        filter's generation - and answers an unchanged frame from the
        previous pass's arrays: no copy, no scan, no allocation. A log write
        bumps the version, a resize moves the capacity, a find or
        clear-filter bumps the generation, and a session reset swaps the
        buffer; any of them misses, and the read runs again.

        The arrays grow with the buffer's capacity and never shrink, so a
        terminal whose log buffer is resized reallocates once rather than per
        frame. Main-thread only, like every other consumer of the view: the
        buffer's own reads are the thread-safe part.
     */
    internal sealed class LogWindowCache
    {
        /*
            Floor for the reused log window, so a small configured buffer does
            not reallocate on every capacity change around it.
         */
        private const int MinimumWindowSize = 16;

        /*
            Whether the most recent Read answered from the memo instead of
            reading the buffer. Diagnostics for the idle-console contract: a
            pass over an unchanged buffer and query reports true (see
            LogWindowCacheTests).
         */
        public bool LastReadReused { get; private set; }

        public int SourceCount => _readCount;

        private readonly LogFilter _filter;

        /*
            The log window the frame reads, sized to the buffer's capacity and
            reused. One copy per changed frame, so the count the view lays out
            and the lines it renders are the same read.
         */
        private LogItem[] _window = Array.Empty<LogItem>();

        /*
            The lines of that window a search keeps, in the same reused form.
            Separate from the window so clearing a search redraws every line
            without the buffer being read a second time.
         */
        private LogItem[] _filteredWindow = Array.Empty<LogItem>();

        private LogItem[] _collapsedWindow = Array.Empty<LogItem>();
        private int[] _repeatCounts = Array.Empty<int>();
        private int[] _sourceRows = Array.Empty<int>();
        private bool _collapseValid;
        private bool _lastReadCollapsed;
        private int _collapsedCount;
        private int _readWindowCount;

        private CommandLog _readBuffer;
        private long _readVersion;
        private int _readCapacity;
        private long _readFilterGeneration;
        private int _readCount;
        private bool _readFiltered;

        public LogWindowCache(LogFilter filter)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        }

        /*
            Reads the lines the log view draws this pass: the buffer's window,
            or the ones of it a search keeps. Hands back the array holding
            them and how many there are, so the caller reads the count and the
            lines from one place - a count from a filtered read and lines from
            the unfiltered window would draw lines the count never promised.

            No search means no second pass and no second array: the window is
            already the answer, which is why the filtered array only ever grows
            to the window's size.
         */
        public int Read(CommandLog buffer, out LogItem[] rendered)
        {
            _lastReadCollapsed = false;
            if (buffer == null)
            {
                _readBuffer = null;
                _readCount = 0;
                _collapseValid = false;
                LastReadReused = false;
                rendered = Array.Empty<LogItem>();
                return 0;
            }

            long version = buffer.Version;
            int capacity = buffer.Capacity;
            long filterGeneration = _filter.Generation;
            if (ReferenceEquals(buffer, _readBuffer) && version == _readVersion && capacity == _readCapacity && filterGeneration == _readFilterGeneration)
            {
                LastReadReused = true;
                rendered = _readFiltered ? _filteredWindow : _window;
                return _readCount;
            }

            LastReadReused = false;
            _collapseValid = false;
            if (_window.Length < capacity)
            {
                _window = new LogItem[Math.Max(capacity, MinimumWindowSize)];
            }

            /*
                One consistent read of the whole window, not a count followed
                by a per-line index: a background thread's Debug.Log arrives
                through the same buffer this reads, and a count and the lines
                read under it separately could describe two different moments.
                The window is sized to the buffer's capacity, so it holds
                every entry a read can return, and it is written once and
                reused. A search narrows that read to the lines it keeps, and
                the count the view lays out is the count it draws.
             */
            int windowCount = buffer.CopyTo(_window);
            int count;
            if (!_filter.IsActive)
            {
                rendered = _window;
                count = windowCount;
            }
            else
            {
                if (_filteredWindow.Length < _window.Length)
                {
                    _filteredWindow = new LogItem[_window.Length];
                }

                rendered = _filteredWindow;
                count = _filter.Apply(_window, windowCount, _filteredWindow);
            }

            _readWindowCount = windowCount;
            _readBuffer = buffer;
            _readVersion = version;
            _readCapacity = capacity;
            _readFilterGeneration = filterGeneration;
            _readCount = count;
            _readFiltered = _filter.IsActive;
            return count;
        }

        public int Read(CommandLog buffer, bool collapse, out LogItem[] rendered)
        {
            int count = Read(buffer, out LogItem[] source);
            if (!collapse || buffer == null)
            {
                rendered = source;
                return count;
            }

            if (!_collapseValid)
            {
                LastReadReused = false;
                int capacity = _window.Length;
                if (_collapsedWindow.Length < capacity)
                {
                    // The rebuild replaces every live slot; preserving old contents would add unused copies.
                    _collapsedWindow = new LogItem[capacity];
                    _repeatCounts = new int[capacity];
                    _sourceRows = new int[capacity];
                }

                _collapsedCount = 0;
                int windowIndex = 0;
                int? previousWindowIndex = null;
                for (int i = 0; i < count; ++i)
                {
                    LogItem item = source[i];
                    while (windowIndex < _readWindowCount && _window[windowIndex].Sequence != item.Sequence)
                    {
                        ++windowIndex;
                    }

                    // Hidden entries break runs even if the surviving messages have equal content.
                    if (previousWindowIndex.HasValue && previousWindowIndex.Value + 1 == windowIndex && item.Equals(_collapsedWindow[_collapsedCount - 1]))
                    {
                        ++_repeatCounts[_collapsedCount - 1];
                    }
                    else
                    {
                        _collapsedWindow[_collapsedCount] = item;
                        _repeatCounts[_collapsedCount] = 1;
                        ++_collapsedCount;
                    }
                    _sourceRows[i] = _collapsedCount - 1;
                    previousWindowIndex = windowIndex;
                    ++windowIndex;
                }
                _collapseValid = true;
            }

            _lastReadCollapsed = true;
            rendered = _collapsedWindow;
            return _collapsedCount;
        }

        public int GetRepeatCount(int renderedIndex)
        {
            int count = _lastReadCollapsed ? _collapsedCount : _readCount;
            if (renderedIndex < 0 || count <= renderedIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(renderedIndex));
            }
            return _lastReadCollapsed ? _repeatCounts[renderedIndex] : 1;
        }

        public int GetRenderedIndex(int sourceIndex)
        {
            if (sourceIndex < 0 || _readCount <= sourceIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            }
            return _lastReadCollapsed ? _sourceRows[sourceIndex] : sourceIndex;
        }
    }
}
