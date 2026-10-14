# Shortcuts for building TxSuite; the rest is dotnet (see the README).

# make appimage [RID=linux-arm64]: an AppImage of this checkout in out/, TxSuite-<version>-<rid>.AppImage (packaging/linux/appimage.sh)
RID ?= linux-x64

.PHONY: appimage
appimage:
	bash packaging/linux/appimage.sh $(RID)
