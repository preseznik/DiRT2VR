bool InPrivateGameCopy() {
    const auto image = trace::ModulePath();
    const auto expected = trace::Environment(L"DIRT2VR_DIRECT_LAB_EXE");
    constexpr wchar_t suffix[] = L"\\Direct\\game\\dirt2_game.exe";
    const size_t length = _countof(suffix) - 1;
    return trace::AbsolutePath(expected) && image.size() > length &&
           !_wcsicmp(image.c_str(), expected.c_str()) &&
           !_wcsicmp(image.c_str() + image.size() - length, suffix);
}
