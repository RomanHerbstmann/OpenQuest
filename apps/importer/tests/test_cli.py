from openquest_importer.cli import build_parser


def test_sync_accepts_schema_change_flag():
    args = build_parser().parse_args(["sync", "de-muenster-trees", "--accept-schema-change"])
    assert args.accept_schema_change is True and args.force is False


def test_schema_change_is_not_accepted_by_default():
    args = build_parser().parse_args(["sync", "--all"])
    assert args.accept_schema_change is False
