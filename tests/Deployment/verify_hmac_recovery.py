"""Same full firm recovery acceptance with a quota-bound HMAC and all delivery gates closed."""
import sys
import zipfile
from verify_recovery import fixture, verify

if __name__=='__main__':
    try:
        options=fixture.arguments();options.hmac=True;verify(options)
    except (RuntimeError,ValueError,TypeError,KeyError,OSError,zipfile.BadZipFile) as error:
        print(str(error) if isinstance(error,RuntimeError) else 'HMAC kurtarma kabulü başarısız; özel ayrıntılar gizlendi.',file=sys.stderr);sys.exit(1)
